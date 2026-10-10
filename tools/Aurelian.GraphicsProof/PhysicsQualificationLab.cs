using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Physics3D;
using Aurelian.Physics3D.Bepu;

internal static class PhysicsQualificationLab
{
    internal sealed record Timing(int Samples, double MedianMilliseconds, double P95Milliseconds);
    internal sealed record Result(string Case, string Profile, bool MeetsCriteria, string Criteria,
        Dictionary<string, double> Metrics, Timing? Step = null, string? StateHash = null, bool RequiredProfile = true);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };

    public static void Run(string output, string? selectedCase, bool headless)
    {
        string[] cases = ["ccd", "stacks", "rotation", "platform", "joints", "lifecycle", "sleep", "replay", "cloth"];
        if (selectedCase is not null && !cases.Contains(selectedCase, StringComparer.Ordinal))
        {
            throw new ArgumentException("Unknown --case. Choose: " + string.Join(", ", cases));
        }
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"ExperimentCompleted\":false}");
        var results = new List<Result>();
        foreach (string name in cases.Where(name => selectedCase is null || name == selectedCase))
        {
            IEnumerable<Result> measurements = name switch
            {
                "ccd" => Ccd().Concat(PhysicsRawCcdControl.Measure()),
                "stacks" => Stacks(),
                "rotation" => Rotation(),
                "platform" => Platform(),
                "joints" => Joints(),
                "lifecycle" => Lifecycle(),
                "sleep" => Sleep(),
                "replay" => Replay(),
                "cloth" => PhysicsClothSpecimen.Measure(output, headless),
                _ => throw new UnreachableException(),
            };
            foreach (Result result in measurements)
            {
                Require(result.Metrics.Values.All(double.IsFinite), "Nonfinite metric in " + result.Case);
                results.Add(result);
                File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, Json));
                Console.WriteLine($"PHYSICS_LAB {result.Case} {result.Profile} meets_criteria={result.MeetsCriteria} median_ms={result.Step?.MedianMilliseconds:F3}");
            }
        }
        // Completion is different from every deliberately stressed profile passing.
        Require(results.Count > 0, "No physics experiments ran.");
        foreach (string name in results.Select(result => result.Case).Distinct())
        {
            Require(results.Any(result => result.Case == name && result.MeetsCriteria),
                "No positive control passed for " + name + "; see results.json.");
        }
        Require(results.Where(result => result.RequiredProfile).All(result => result.MeetsCriteria),
            "A required profile failed; see results.json.");
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            ExperimentCompleted = true,
            AllProfilesMeetCriteria = results.All(result => result.MeetsCriteria),
            Backend = "BEPU 2.4.0",
            Configuration = configuration,
            Runtime = Environment.Version.ToString(),
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            LogicalProcessors = Environment.ProcessorCount,
            Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime default",
            NativeClothCaptures = !headless && (selectedCase is null || selectedCase == "cloth"),
            TimingPolicy = "Owned Step includes contact collection. Snapshot/metric sampling excluded. First 60 steps excluded from timing; timings are observations, not CI thresholds.",
            Limits = new[]
            {
                "No GPU physics, hair, volumetric soft bodies or dynamic character controller qualified.",
                "Replay reconstructs a fresh world from authored commands; no warm solver checkpoint restore or cross-platform lockstep claim.",
                "Cloth is a particle/distance-constraint experiment; its rendered triangles are not collision surfaces.",
                "Pool memory is the BEPU main pool reservation; worker pools and managed memory are not included.",
                "Native captures are offscreen Vulkan renders, not desktop interaction qualification.",
            },
            Results = results,
        }, Json));
        Console.WriteLine("AURELIAN_PHYSICS_LAB_COMPLETED " + evidence);
    }

    private static IEnumerable<Result> Ccd()
    {
        foreach (float delta in new[] { 1f / 30, 1f / 60, 1f / 120 })
        {
            foreach (float speed in new[] { 10f, 100f, 500f })
            {
                foreach (var profile in new[]
                {
                    (Mode: PhysicsContinuity3D.Passive, Substeps: 1, Hertz: 30f, Sweep: .0001f),
                    (Mode: PhysicsContinuity3D.Continuous, Substeps: 1, Hertz: 30f, Sweep: .0001f),
                    (Mode: PhysicsContinuity3D.Continuous, Substeps: 4, Hertz: 300f, Sweep: .0001f),
                    (Mode: PhysicsContinuity3D.Continuous, Substeps: 4, Hertz: 1000f, Sweep: .000001f),
                })
                {
                    using var world = new BepuPhysicsWorld3D(new()
                    {
                        Gravity = Vector3.Zero,
                        FixedDeltaSeconds = delta,
                        Substeps = profile.Substeps,
                        ContactSpring = new(profile.Hertz, 1),
                    });
                    world.AddBody(new("wall", new PhysicsShape3D.Box(new(.01f, 10, 10)), PhysicsPose3D.At(Vector3.Zero))
                    {
                        MotionType = PhysicsMotionType3D.Static,
                    });
                    world.AddBody(new("shot", new PhysicsShape3D.Sphere(.05f), PhysicsPose3D.At(new(-1, 0, 0)))
                    {
                        Continuity = profile.Mode,
                        MinimumSweepSeconds = profile.Sweep,
                        SweepConvergenceSeconds = profile.Sweep,
                        Velocity = new(new(speed, 0, 0), Vector3.Zero),
                    });
                    float maximumX = -1;
                    int ticks = (int)Math.Ceiling(2 / (speed * delta)) + 8;
                    for (int tick = 1; tick <= ticks; tick++)
                    {
                        world.Step(new(tick));
                        maximumX = Math.Max(maximumX, world.GetBody("shot").Pose.Position.X);
                    }
                    var body = world.GetBody("shot");
                    yield return new("ccd", $"{profile.Mode}/{profile.Substeps}sub/{profile.Hertz}Hz/sweep-{profile.Sweep}/{speed}mps/{1 / delta:F0}fps",
                        maximumX <= -.045f && body.Velocity.Linear.X < 1,
                        "Sphere stays on approach side of 1 cm wall, <=1 cm penetration; final forward vx <1 m/s. Rebound is reported, not rejected.",
                        new() { ["MaximumXMetres"] = maximumX, ["FinalVelocityX"] = body.Velocity.Linear.X },
                        RequiredProfile: profile.Hertz == 1000);
                }
            }
        }
        // A sweep must not turn a close miss into a collision.
        using var miss = new BepuPhysicsWorld3D(new() { Gravity = Vector3.Zero, Substeps = 4, ContactSpring = new(300, 1) });
        miss.AddBody(new("wall", new PhysicsShape3D.Box(new(.01f, 1, 1)), PhysicsPose3D.At(Vector3.Zero))
        {
            MotionType = PhysicsMotionType3D.Static,
        });
        miss.AddBody(new("shot", new PhysicsShape3D.Sphere(.05f), PhysicsPose3D.At(new(-1, .57f, 0)))
        {
            Continuity = PhysicsContinuity3D.Continuous,
            Velocity = new(new(500, 0, 0), Vector3.Zero),
        });
        miss.Step(new(1));
        float velocity = miss.GetBody("shot").Velocity.Linear.X;
        yield return new("ccd", "continuous-near-miss", Math.Abs(velocity - 500) < .01f,
            "Sphere passing 2 cm outside wall retains 500 m/s velocity.", new() { ["FinalVelocityX"] = velocity });
    }

    private static IEnumerable<Result> Stacks()
    {
        foreach (int substeps in new[] { 1, 4, 16 })
        {
            foreach (float loadMass in new[] { 1f, 100f })
            {
                int iterations = substeps == 16 ? 32 : 8;
                using var world = new BepuPhysicsWorld3D(new()
                {
                    Substeps = substeps,
                    SolverIterations = iterations,
                    ContactSpring = new(substeps == 16 ? 120 : 30, 1),
                    MaximumRecoveryVelocity = substeps == 16 ? 10 : 2,
                });
                Floor(world, 1000);
                for (int index = 0; index < 16; index++)
                {
                    world.AddBody(new($"box-{index:D2}", new PhysicsShape3D.Box(Vector3.One),
                        PhysicsPose3D.At(new(0, .5f + index * 1.001f, 0))) { Mass = index == 15 ? loadMass : 1 });
                }
                var samples = new List<double>();
                float maximumPenetration = 0;
                float finalDrift = 0;
                for (int tick = 1; tick <= 600; tick++)
                {
                    TimedStep(world, tick, samples);
                    foreach (var body in world.CaptureSnapshot().Bodies.Where(body => body.MotionType == PhysicsMotionType3D.Dynamic))
                    {
                        body.Pose.Validate();
                        body.Velocity.Validate();
                        Matrix4x4 rotation = Matrix4x4.CreateFromQuaternion(body.Pose.Orientation);
                        float supportY = .5f * (Math.Abs(rotation.M12) + Math.Abs(rotation.M22) + Math.Abs(rotation.M32));
                        maximumPenetration = Math.Max(maximumPenetration, supportY - body.Pose.Position.Y);
                        if (tick == 600)
                        {
                            finalDrift = Math.Max(finalDrift, new Vector2(body.Pose.Position.X, body.Pose.Position.Z).Length());
                        }
                    }
                }
                float top = world.GetBody("box-15").Pose.Position.Y;
                yield return new("stacks", $"16-boxes/{substeps}sub/{iterations}iterations/top-mass-{loadMass}",
                    maximumPenetration < .05f && finalDrift < .25f && top > 14.5f,
                    "16 m tower retains top above 14.5 m; floor penetration <5 cm; lateral drift <25 cm after 10 seconds.",
                    new()
                    {
                        ["MaximumFloorPenetration"] = maximumPenetration,
                        ["FinalLateralDrift"] = finalDrift,
                        ["FinalTopHeight"] = top,
                        ["ContactFrequencyHz"] = world.Options.ContactSpring.Frequency,
                        ["MaximumRecoveryVelocity"] = world.Options.MaximumRecoveryVelocity,
                    }, Summarize(samples), RequiredProfile: loadMass == 1 || substeps == 16);
            }
        }
    }

    private static IEnumerable<Result> Joints()
    {
        foreach (int substeps in new[] { 1, 4, 16 })
        {
            foreach (float loadMass in new[] { 1f, 100f })
            {
                int iterations = substeps == 16 ? 32 : 8;
                using var world = new BepuPhysicsWorld3D(new() { Substeps = substeps, SolverIterations = iterations });
                for (int index = 0; index <= 16; index++)
                {
                    world.AddBody(new($"link-{index:D2}", new PhysicsShape3D.Sphere(.12f), PhysicsPose3D.At(new(0, 9 - index * .5f, 0)))
                    {
                        MotionType = index == 0 ? PhysicsMotionType3D.Kinematic : PhysicsMotionType3D.Dynamic,
                        Mass = index == 16 ? loadMass : 1,
                    });
                    if (index > 0)
                    {
                        world.AddJoint(new PhysicsJoint3D.BallSocket($"socket-{index}", $"link-{index - 1:D2}", $"link-{index:D2}",
                            new(0, -.25f, 0), new(0, .25f, 0)) { Spring = new(substeps == 16 ? 120 : 30, 1) });
                    }
                }
                world.ApplyImpulse("link-16", new(2, 0, 0));
                var samples = new List<double>();
                float maximumError = 0;
                for (int tick = 1; tick <= 600; tick++)
                {
                    TimedStep(world, tick, samples);
                    for (int index = 1; index <= 16; index++)
                    {
                        var a = world.GetBody($"link-{index - 1:D2}").Pose;
                        var b = world.GetBody($"link-{index:D2}").Pose;
                        a.Validate();
                        b.Validate();
                        float error = Vector3.Distance(a.Position + Vector3.Transform(new Vector3(0, -.25f, 0), a.Orientation),
                            b.Position + Vector3.Transform(new Vector3(0, .25f, 0), b.Orientation));
                        maximumError = Math.Max(maximumError, error);
                    }
                }
                yield return new("joints", $"16-link-chain/{substeps}sub/{iterations}iterations/end-mass-{loadMass}", maximumError < .05f,
                    "Maximum local-anchor separation <5 cm across 10 seconds.",
                    new() { ["MaximumAnchorError"] = maximumError, ["ConstraintFrequencyHz"] = substeps == 16 ? 120 : 30 },
                    Summarize(samples), RequiredProfile: loadMass == 1 || substeps == 16);
            }
        }
        using var motor = new BepuPhysicsWorld3D(new() { Gravity = Vector3.Zero });
        motor.AddBody(new("anchor", new PhysicsShape3D.Sphere(.1f), PhysicsPose3D.At(Vector3.Zero)) { MotionType = PhysicsMotionType3D.Kinematic });
        motor.AddBody(new("blade", new PhysicsShape3D.Box(new(3, .1f, .1f)), PhysicsPose3D.At(Vector3.Zero)));
        motor.AddJoint(new PhysicsJoint3D.Hinge("pivot", "anchor", "blade", Vector3.Zero, Vector3.Zero, Vector3.UnitY, Vector3.UnitY));
        motor.AddJoint(new PhysicsJoint3D.AngularMotor("drive", "anchor", "blade", Vector3.UnitY, 4, 100));
        for (int tick = 1; tick <= 120; tick++)
        {
            motor.Step(new(tick));
        }
        var blade = motor.GetBody("blade");
        float trackingError = Math.Abs(blade.Velocity.Angular.Y + 4);
        yield return new("joints", "hinge-motor", trackingError < .02f && blade.Pose.Position.Length() < .001f,
            "Stationary A minus rotating B tracks +4 rad/s within .02; pivot error <1 mm.",
            new() { ["VelocityErrorRadiansPerSecond"] = trackingError, ["PivotError"] = blade.Pose.Position.Length() });
    }

    private static IEnumerable<Result> Rotation()
    {
        foreach (float speed in new[] { .5f, 50f })
        {
            foreach (int substeps in new[] { 1, 4 })
            {
                using var world = new BepuPhysicsWorld3D(new()
                {
                    Gravity = Vector3.Zero,
                    Substeps = substeps,
                    ContactSpring = new(substeps == 4 ? 120 : 30, 1),
                });
                Floor(world);
                world.AddBody(new("blade", new PhysicsShape3D.Box(new(4, .1f, .1f)), PhysicsPose3D.At(new(0, 1.2f, 0)))
                {
                    Continuity = substeps == 4 ? PhysicsContinuity3D.Continuous : PhysicsContinuity3D.Passive,
                    MinimumSweepSeconds = .000001f,
                    SweepConvergenceSeconds = .000001f,
                    Velocity = new(Vector3.Zero, new(0, 0, speed)),
                });
                float penetration = 0;
                var samples = new List<double>();
                for (int tick = 1; tick <= 240; tick++)
                {
                    TimedStep(world, tick, samples);
                    var blade = world.GetBody("blade");
                    blade.Pose.Validate();
                    blade.Velocity.Validate();
                    var rotation = Matrix4x4.CreateFromQuaternion(blade.Pose.Orientation);
                    float supportY = 2 * Math.Abs(rotation.M12) + .05f * (Math.Abs(rotation.M22) + Math.Abs(rotation.M32));
                    penetration = Math.Max(penetration, supportY - blade.Pose.Position.Y);
                }
                yield return new("rotation", $"4m-blade/{speed}radps/{substeps}sub", penetration < .05f,
                    "Rotating blade floor penetration stays below 5 cm across four seconds.",
                    new() { ["MaximumFloorPenetration"] = penetration }, Summarize(samples), RequiredProfile: speed == .5f);
            }
        }
    }

    private static IEnumerable<Result> Platform()
    {
        using var world = new BepuPhysicsWorld3D(new() { Substeps = 2 });
        Floor(world);
        world.AddBody(new("platform", new PhysicsShape3D.Box(new(6, .2f, 6)), PhysicsPose3D.At(new(0, 1, 0)))
        {
            MotionType = PhysicsMotionType3D.Kinematic,
            Friction = 1,
        });
        world.AddBody(new("rider", new PhysicsShape3D.Box(Vector3.One), PhysicsPose3D.At(new(0, 2, 0))) { Friction = 1 });
        for (int tick = 1; tick <= 180; tick++)
        {
            world.Step(new(tick));
        }
        world.SetMotion("platform", world.GetBody("platform").Pose, new(Vector3.UnitX, Vector3.Zero));
        for (int tick = 181; tick <= 300; tick++)
        {
            world.Step(new(tick));
        }
        var platform = world.GetBody("platform");
        var rider = world.GetBody("rider");
        float lag = Math.Abs(platform.Pose.Position.X - rider.Pose.Position.X);
        yield return new("platform", "kinematic-platform/dynamic-box", lag < .2f && rider.Pose.Position.Y > 1.55f,
            "Rider remains supported and follows platform within 20 cm after two seconds at 1 m/s. This is not a character-controller qualification.",
            new() { ["RiderLag"] = lag, ["RiderHeight"] = rider.Pose.Position.Y, ["RiderVelocityX"] = rider.Velocity.Linear.X });

        world.AddBody(new("pusher", new PhysicsShape3D.Capsule(.3f, 1), PhysicsPose3D.At(new(-3, .8f, 8)))
        {
            MotionType = PhysicsMotionType3D.Kinematic,
            Velocity = new(Vector3.UnitX * 2, Vector3.Zero),
        });
        world.AddBody(new("crate", new PhysicsShape3D.Box(Vector3.One), PhysicsPose3D.At(new(0, .5f, 8))));
        for (int tick = 301; tick <= 420; tick++)
        {
            world.Step(new(tick));
        }
        var crate = world.GetBody("crate");
        yield return new("platform", "kinematic-capsule-push", crate.Pose.Position.X > 1 && crate.Pose.Position.Y > .45f,
            "An authored kinematic capsule pushes a dynamic crate >1 m without losing floor support; gameplay movement policy remains separate.",
            new() { ["CrateX"] = crate.Pose.Position.X, ["CrateHeight"] = crate.Pose.Position.Y });
    }

    private static IEnumerable<Result> Lifecycle()
    {
        using var world = new BepuPhysicsWorld3D(new() { Gravity = Vector3.Zero });
        long warmPool = 0;
        for (int batch = 0; batch < 100; batch++)
        {
            for (int index = 0; index < 128; index++)
            {
                world.AddBody(new($"body-{index}", new PhysicsShape3D.Sphere(.1f), PhysicsPose3D.At(new(index, 0, 0))));
                if (index > 0)
                {
                    world.AddJoint(new PhysicsJoint3D.DistanceLimit($"joint-{index}", $"body-{index - 1}", $"body-{index}", .5f, 1));
                }
            }
            world.Step(new(batch + 1));
            for (int index = 0; index < 128; index++)
            {
                Require(world.RemoveBody($"body-{index}"), "Churn failed to remove body.");
            }
            Require(world.JointCount == 0 && world.BodyCount == 0, "Churn left bodies or joints behind.");
            if (batch == 9)
            {
                warmPool = world.ReservedMainPoolBytes;
            }
        }
        yield return new("lifecycle", "100-batches/128-bodies/127-joints", world.ReservedMainPoolBytes == warmPool,
            "All 12,800 removals clean attached joints; main pool reservation stops growing after 10 batches.",
            new() { ["WarmMainPoolBytes"] = warmPool, ["FinalMainPoolBytes"] = world.ReservedMainPoolBytes });
    }

    private static IEnumerable<Result> Sleep()
    {
        using var world = new BepuPhysicsWorld3D();
        Floor(world);
        for (int index = 0; index < 64; index++)
        {
            world.AddBody(new($"body-{index}", new PhysicsShape3D.Box(Vector3.One), PhysicsPose3D.At(new(index % 8 * 2 - 7, 1.5f, index / 8 * 2 - 7))));
        }
        for (int tick = 1; tick <= 600; tick++)
        {
            world.Step(new(tick));
        }
        int sleeping = world.CaptureSnapshot().Bodies.Count(body => body.MotionType == PhysicsMotionType3D.Dynamic && !body.Awake);
        var samples = new List<double>();
        for (int tick = 601; tick <= 780; tick++)
        {
            TimedStep(world, tick, samples);
        }
        world.ApplyImpulse("body-0", new(0, 3, 0));
        world.Step(new(781));
        var kicked = world.GetBody("body-0");
        yield return new("sleep", "64-separated-boxes", sleeping == 64 && kicked.Awake && kicked.Velocity.Linear.Y > 2,
            "All 64 bodies sleep after 10 seconds; impulse wakes selected body and produces upward velocity >2 m/s.",
            new() { ["SleepingBodies"] = sleeping, ["KickedVelocityY"] = kicked.Velocity.Linear.Y }, Summarize(samples));
    }

    private static IEnumerable<Result> Replay()
    {
        foreach (int workers in new[] { 1, Math.Min(4, Environment.ProcessorCount) }.Distinct())
        {
            string[] first = Record(workers);
            string[] second = Record(workers);
            int mismatches = first.Zip(second).Count(pair => pair.First != pair.Second);
            yield return new("replay", $"fresh-world-command-replay/{workers}-workers", mismatches == 0,
                "Serialized pose/velocity/awake state matches at every tick for two fresh worlds on this machine and worker count.",
                new() { ["Ticks"] = first.Length, ["MismatchedTicks"] = mismatches }, StateHash: first[^1]);
        }

        static string[] Record(int workers)
        {
            using var world = new BepuPhysicsWorld3D(new() { WorkerCount = workers, Substeps = 2 });
            Floor(world);
            for (int index = 0; index < 64; index++)
            {
                world.AddBody(new($"body-{index:D2}", new PhysicsShape3D.Box(new(.8f)),
                    PhysicsPose3D.At(new(index % 8 - 3.5f, 2 + index / 8 * .9f, 0))));
            }
            var hashes = new string[300];
            for (int tick = 1; tick <= hashes.Length; tick++)
            {
                if (tick == 100)
                {
                    world.ApplyImpulse("body-63", new(2, 4, 1));
                }
                if (tick == 150)
                {
                    world.RemoveBody("body-00");
                    world.AddBody(new("replacement", new PhysicsShape3D.Sphere(.3f), PhysicsPose3D.At(new(1, 6, 1))));
                }
                world.Step(new(tick));
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(world.CaptureSnapshot(), Json);
                hashes[tick - 1] = Convert.ToHexString(SHA256.HashData(bytes));
            }
            return hashes;
        }
    }

    internal static void Floor(BepuPhysicsWorld3D world, float size = 40)
    {
        world.AddBody(new("floor", new PhysicsShape3D.Box(new(size, .5f, size)), PhysicsPose3D.At(new(0, -.25f, 0)))
        {
            MotionType = PhysicsMotionType3D.Static,
        });
    }

    internal static PhysicsStepResult3D TimedStep(BepuPhysicsWorld3D world, int tick, List<double> samples)
    {
        long start = Stopwatch.GetTimestamp();
        PhysicsStepResult3D result = world.Step(new(tick));
        if (tick > 60)
        {
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        return result;
    }

    internal static Timing Summarize(List<double> values)
    {
        double[] sorted = values.Order().ToArray();
        return new(sorted.Length, sorted[sorted.Length / 2], sorted[(int)Math.Ceiling(sorted.Length * .95) - 1]);
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
