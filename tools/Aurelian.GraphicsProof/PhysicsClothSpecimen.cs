using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.NativeComposition;
using Aurelian.Physics3D;
using Aurelian.Physics3D.Bepu;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

/// <summary>Research fixture only: particle contacts and distance limits are not a production fabric solver.</summary>
internal sealed class PhysicsClothSpecimen : IDisposable
{
    private const int Side = 17;
    private const float Spacing = .2f;
    private const float Radius = .045f;
    private static readonly Vector3 Support = new(0, 1.65f, -.8f);
    private const float SupportRadius = .85f;
    private readonly List<(int A, int B, float RestLength)> edges = [];
    private readonly int[] indices;
    private readonly Vector3[] initial;
    private readonly BepuPhysicsWorld3D world;
    private readonly float constraintFrequency;

    private PhysicsClothSpecimen(int substeps, int workers, bool selfCollision, bool stiffSprings)
    {
        world = new(new()
        {
            Substeps = substeps,
            WorkerCount = workers,
            FixedDeltaSeconds = stiffSprings && substeps >= 8 ? 1f / 120 : 1f / 60,
            SolverIterations = stiffSprings && substeps >= 8 ? 16 : 8,
            ContactSpring = new(stiffSprings ? 90 : 30, 1),
        });
        constraintFrequency = stiffSprings ? 100 : 40;
        PhysicsQualificationLab.Floor(world);
        world.AddBody(new("support", new PhysicsShape3D.Sphere(SupportRadius), PhysicsPose3D.At(Support))
        {
            MotionType = PhysicsMotionType3D.Static,
            Friction = .5f,
        });
        initial = new Vector3[Side * Side];
        for (int row = 0; row < Side; row++)
        {
            for (int column = 0; column < Side; column++)
            {
                int index = row * Side + column;
                Vector3 position = new((column - 8) * Spacing, 3.5f, (row - 8) * Spacing);
                initial[index] = position;
                world.AddBody(new(Id(index), new PhysicsShape3D.Sphere(Radius), PhysicsPose3D.At(position))
                {
                    MotionType = Pinned(index) ? PhysicsMotionType3D.Kinematic : PhysicsMotionType3D.Dynamic,
                    Mass = .025f,
                    Friction = .5f,
                    Layer = 2,
                    Mask = selfCollision ? uint.MaxValue : 1,
                });
            }
        }
        var triangles = new List<int>();
        for (int row = 0; row < Side; row++)
        {
            for (int column = 0; column < Side; column++)
            {
                int a = row * Side + column;
                if (column + 1 < Side) AddEdge(a, a + 1);
                if (row + 1 < Side) AddEdge(a, a + Side);
                if (column + 1 < Side && row + 1 < Side)
                {
                    AddEdge(a, a + Side + 1);
                    AddEdge(a + 1, a + Side);
                    triangles.AddRange([a, a + Side, a + 1, a + 1, a + Side, a + Side + 1]);
                }
                // Two-hop distances resist severe folding, but are not an angle-based bending energy.
                if (column + 2 < Side) AddEdge(a, a + 2);
                if (row + 2 < Side) AddEdge(a, a + 2 * Side);
            }
        }
        indices = triangles.ToArray();
        if (selfCollision)
        {
            for (int a = 0; a < initial.Length; a++)
            {
                for (int b = a + 1; b < initial.Length; b++)
                {
                    if (Math.Abs(a / Side - b / Side) <= 2 && Math.Abs(a % Side - b % Side) <= 2)
                    {
                        world.SetCollisionEnabled(Id(a), Id(b), false);
                    }
                }
            }
        }
    }

    private void AddEdge(int a, int b)
    {
        float rest = Vector3.Distance(initial[a], initial[b]);
        world.AddJoint(new PhysicsJoint3D.DistanceLimit($"edge-{a}-{b}", Id(a), Id(b), rest * .1f, rest)
        {
            Spring = new(constraintFrequency, 1),
        });
        edges.Add((a, b, rest));
    }

    internal static IEnumerable<PhysicsQualificationLab.Result> Measure(string output, bool headless)
    {
        foreach (var profile in new[]
        {
            (Substeps: 1, Workers: 1, Self: false, StiffSprings: false),
            (Substeps: 4, Workers: 1, Self: false, StiffSprings: false),
            (Substeps: 4, Workers: 1, Self: false, StiffSprings: true),
            (Substeps: 8, Workers: 1, Self: false, StiffSprings: true),
            (Substeps: 8, Workers: Math.Min(4, Environment.ProcessorCount), Self: false, StiffSprings: true),
            (Substeps: 8, Workers: 1, Self: true, StiffSprings: true),
        }.Distinct())
        {
            using var specimen = new PhysicsClothSpecimen(profile.Substeps, profile.Workers, profile.Self, profile.StiffSprings);
            bool capture = !headless && profile.StiffSprings && profile.Substeps == 8 && profile.Workers == 1 && !profile.Self;
            using var rendering = capture ? new CaptureSession(output) : null;
            rendering?.Capture(specimen, "cloth-initial");
            var samples = new List<double>();
            double maximumStretch = 1;
            double maximumNodePenetration = 0;
            double maximumTriangleSamplePenetration = 0;
            double maximumPinError = 0;
            long selfContactObservations = 0;
            int tickScale = specimen.world.Options.FixedDeltaSeconds < .01f ? 2 : 1;
            for (int tick = 1; tick <= 1200 * tickScale; tick++)
            {
                specimen.MovePins(tick);
                PhysicsStepResult3D step = PhysicsQualificationLab.TimedStep(specimen.world, tick, samples);
                selfContactObservations += step.Contacts.Count(pair => pair.BodyA.StartsWith("particle-", StringComparison.Ordinal)
                    && pair.BodyB.StartsWith("particle-", StringComparison.Ordinal));
                Vector3[] positions = specimen.Positions();
                foreach (var edge in specimen.edges)
                {
                    maximumStretch = Math.Max(maximumStretch, Vector3.Distance(positions[edge.A], positions[edge.B]) / edge.RestLength);
                }
                foreach (Vector3 position in positions)
                {
                    maximumNodePenetration = Math.Max(maximumNodePenetration,
                        Math.Max(Radius - position.Y, SupportRadius + Radius - Vector3.Distance(position, Support)));
                }
                for (int index = 0; index < specimen.indices.Length; index += 3)
                {
                    Vector3 centroid = (positions[specimen.indices[index]] + positions[specimen.indices[index + 1]] + positions[specimen.indices[index + 2]]) / 3;
                    maximumTriangleSamplePenetration = Math.Max(maximumTriangleSamplePenetration,
                        SupportRadius - Vector3.Distance(centroid, Support));
                }
                foreach (int pin in new[] { 0, Side - 1 })
                {
                    maximumPinError = Math.Max(maximumPinError, Vector3.Distance(positions[pin], specimen.PinTarget(pin, tick)));
                }
                if (tick == 300 * tickScale || tick == 450 * tickScale || tick == 600 * tickScale || tick == 1200 * tickScale)
                {
                    rendering?.Capture(specimen, "cloth-tick-" + tick);
                }
            }
            Vector3[] final = specimen.Positions();
            double centerSag = 3.5 - final[Side * Side / 2].Y;
            if (capture)
            {
                File.WriteAllText(Path.Combine(output, "cloth-snapshot.json"), JsonSerializer.Serialize(new
                {
                    Snapshot = specimen.world.CaptureSnapshot(),
                    Joints = specimen.world.CaptureJoints().Select(joint => (object)joint).ToArray(),
                    Indices = specimen.indices,
                }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
            }
            yield return new("cloth", $"17x17/{profile.Substeps}sub/{profile.Workers}workers/self-{profile.Self}/stiff-springs-{profile.StiffSprings}",
                maximumStretch < 1.1 && maximumNodePenetration < .03 && maximumPinError < .001 && centerSag > .25,
                "Distance stretch <10%; particle/support or floor penetration <3 cm; pins within 1 mm; center sags >25 cm. Triangle collision is not qualified.",
                new()
                {
                    ["Particles"] = Side * Side,
                    ["Constraints"] = specimen.world.JointCount,
                    ["FixedDeltaSeconds"] = specimen.world.Options.FixedDeltaSeconds,
                    ["SolverIterations"] = specimen.world.Options.SolverIterations,
                    ["ContactFrequencyHz"] = specimen.world.Options.ContactSpring.Frequency,
                    ["ConstraintFrequencyHz"] = specimen.constraintFrequency,
                    ["SelfContactPairObservations"] = selfContactObservations,
                    ["FinalAwakeDynamicParticles"] = specimen.world.CaptureSnapshot().Bodies.Count(body => body.MotionType == PhysicsMotionType3D.Dynamic && body.Awake),
                    ["MaximumStretchRatio"] = maximumStretch,
                    ["MaximumNodePenetration"] = maximumNodePenetration,
                    ["MaximumTriangleCentroidPenetration"] = maximumTriangleSamplePenetration,
                    ["MaximumPinError"] = maximumPinError,
                    ["CenterSag"] = centerSag,
                }, PhysicsQualificationLab.Summarize(samples), RequiredProfile: profile.StiffSprings && profile.Substeps == 8);
        }
    }

    private void MovePins(int tick)
    {
        foreach (int pin in new[] { 0, Side - 1 })
        {
            PhysicsPose3D pose = world.GetBody(Id(pin)).Pose;
            Vector3 target = PinTarget(pin, tick);
            world.SetMotion(Id(pin), pose, new((target - pose.Position) / world.Options.FixedDeltaSeconds, Vector3.Zero));
        }
    }

    private Vector3 PinTarget(int pin, int tick)
    {
        float seconds = tick * world.Options.FixedDeltaSeconds;
        float offset = seconds is > 5 and < 15 ? .35f * MathF.Sin((seconds - 5) * MathF.Tau / 10) : 0;
        return initial[pin] + new Vector3(offset, 0, 0);
    }

    private Vector3[] Positions()
    {
        var result = new Vector3[initial.Length];
        for (int index = 0; index < result.Length; index++)
        {
            PhysicsBodyState3D state = world.GetBody(Id(index));
            state.Pose.Validate();
            state.Velocity.Validate();
            result[index] = state.Pose.Position;
        }
        return result;
    }

    private PlacedSceneMesh Mesh()
    {
        Vector3[] positions = Positions();
        var normals = new Vector3[positions.Length];
        for (int index = 0; index < indices.Length; index += 3)
        {
            int a = indices[index];
            int b = indices[index + 1];
            int c = indices[index + 2];
            Vector3 normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            normals[a] += normal;
            normals[b] += normal;
            normals[c] += normal;
        }
        var vertices = ImmutableArray.CreateBuilder<SceneVertex>(positions.Length);
        for (int index = 0; index < positions.Length; index++)
        {
            Vector3 normal = normals[index].LengthSquared() > 1e-12f ? Vector3.Normalize(normals[index]) : Vector3.UnitY;
            vertices.Add(new(positions[index], normal, Vector4.One));
        }
        return new("cloth", Matrix4x4.Identity, vertices.MoveToImmutable())
        {
            Indices = indices.ToImmutableArray(),
            GeometryIdentity = "physics-lab/cloth17/vertex-correspondence/v1",
            Material = new("cloth") { BaseColor = new(.035f, .35f, .55f, 1), Metallic = 0, Roughness = .9f, DoubleSided = true },
        };
    }

    private static bool Pinned(int index) => index == 0 || index == Side - 1;
    private static string Id(int index) => $"particle-{index:D3}";
    public void Dispose() => world.Dispose();

    private sealed class CaptureSession : IDisposable
    {
        private readonly string output;
        private readonly AurelianVulkanPlant plant;
        private readonly VulkanNativeFrameTarget target;
        private readonly VulkanSolid3DRenderer renderer;
        private readonly SceneInstance scene;
        private readonly Vector3 eye = new(5.8f, 4.8f, 6.5f);
        private readonly Matrix4x4 camera;
        private readonly NativeFrameClearColor clear = new(.035f, .045f, .065f, 1);
        private string? initialHash;

        internal CaptureSession(string output)
        {
            this.output = output;
            var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
                new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian cloth qualification"));
            PhysicsQualificationLab.Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
            plant = initialization.Plant!;
            target = new(plant, 960, 640);
            var assets = new GameAssets();
            renderer = new(plant, assets.Shader("SurfaceSolid3D.v.ts"), target,
                modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
                outputProgram: assets.Shader("ToneMap3D.v.ts"), temporalProgram: assets.Shader("TemporalResolve3D.v.ts"),
                bloomProgram: assets.Shader("Bloom3D.v.ts"), surfacePrograms: new(
                    assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                    assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts")));
            renderer.Settings = Graphics3DSettings.Default with { BloomIntensity = 0, SunIntensity = 3 };
            scene = SceneCompiler.Compile(Scene.World("cloth-lab",
            [
                Scene.Box("floor", new(10, .5f, 10), new(.4f, .44f, .5f, 1), new(0, -.25f, 0)),
                Scene.Box("left-pin", new(.06f, 3.6f, .06f), new(.2f, .22f, .25f, 1), new(-1.6f, 1.75f, -1.6f)),
                Scene.Box("right-pin", new(.06f, 3.6f, .06f), new(.2f, .22f, .25f, 1), new(1.6f, 1.75f, -1.6f)),
            ])).Mount();
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(1, 1.5f, .1f, 50);
            projection.M22 *= -1;
            camera = Matrix4x4.CreateLookAt(eye, new(0, 1.7f, 0), Vector3.UnitY) * projection;
        }

        internal void Capture(PhysicsClothSpecimen specimen, string name)
        {
            SceneFrame frame = scene.Project();
            frame = frame with
            {
                Boxes = frame.Boxes.Select(box => box.Id switch
                {
                    "left-pin" => ProjectPin(box, specimen, 0),
                    "right-pin" => ProjectPin(box, specimen, Side - 1),
                    _ => box,
                }).ToImmutableArray(),
            };
            Native3DScene native = SceneGeometry3D.BuildScene(frame with { Meshes = [specimen.Mesh()] });
            native = native with
            {
                Models = native.Models.Append(SurfaceGraphicsProof.Sphere(Support, SupportRadius,
                    new("support") { BaseColor = new(.65f, .4f, .13f, 1), Metallic = .8f, Roughness = .3f })).ToArray(),
            };
            for (int sample = 0; sample < 12; sample++)
            {
                renderer.Render(native, camera, eye, clear, false);
            }
            var result = renderer.Render(native, camera, eye, clear, true);
            if (initialHash is null)
            {
                initialHash = result.PixelSha256;
            }
            else
            {
                PhysicsQualificationLab.Require(result.PixelSha256 != initialHash, "Cloth simulation did not change native pixels.");
            }
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), 960, 640, result.Pixels!);
            File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new
            {
                Tick = specimen.world.Tick,
                Device = plant.Facts.PhysicalDeviceName,
                ValidationLayers = plant.Facts.EnabledValidationLayers,
                result.PixelSha256,
                result.TriangleCount,
                result.GpuPassTimes,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static PlacedSceneBox ProjectPin(PlacedSceneBox box, PhysicsClothSpecimen specimen, int index)
        {
            Vector3 position = specimen.world.GetBody(Id(index)).Pose.Position;
            return box with { WorldTransform = Matrix4x4.CreateTranslation(position + new Vector3(0, -1.75f, 0)) };
        }

        public void Dispose()
        {
            scene.Dispose();
            renderer.Dispose();
            target.Dispose();
            plant.Dispose();
        }
    }
}
