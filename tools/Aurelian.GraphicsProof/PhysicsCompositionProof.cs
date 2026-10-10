using System.Collections.Immutable;
using System.Diagnostics;
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

internal static class PhysicsCompositionProof
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var initialization = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian physics composition"));
        Require(initialization.Success, string.Join("; ", initialization.Diagnostics.Select(item => item.Message)));
        using var plant = initialization.Plant!;
        var assets = new GameAssets();
        const int width = 960;
        const int height = 640;
        using var target = new VulkanNativeFrameTarget(plant, width, height);
        using var renderer = new VulkanSolid3DRenderer(plant, assets.Shader("SurfaceSolid3D.v.ts"), target,
            modelProgram: assets.Shader("SurfaceModel3D.v.ts"), shadowProgram: assets.Shader("Shadow3D.v.ts"),
            outputProgram: assets.Shader("ToneMap3D.v.ts"), temporalProgram: assets.Shader("TemporalResolve3D.v.ts"),
            bloomProgram: assets.Shader("Bloom3D.v.ts"), surfacePrograms: new(
                assets.Shader("SurfaceResolve3D.v.ts"), assets.Shader("AmbientOcclusion3D.v.ts"),
                assets.Shader("AmbientDenoise3D.v.ts"), assets.Shader("LightTiles3D.v.ts")));
        renderer.Settings = Graphics3DSettings.Default with { BloomIntensity = 0, SunIntensity = 3 };
        using var physics = new BepuPhysicsWorld3D(new() { Substeps = 2 });
        var plan = SceneCompiler.Compile(Scene.World("lab",
        [
            Scene.Box("floor", new(18, .5f, 12), new(.38f, .43f, .52f, 1), new(0, -.25f, 0), SceneCollision.Solid),
        ]));
        using var statics = PhysicsScene3D.AddStatics(physics, SceneSpatial3D.Build(plan));
        using var scene = plan.Mount();
        var assembly = Platform();
        var definition = new PhysicsAssemblyAgentDefinition3D(physics, assembly, "proof/platform/v1")
        {
            Visuals = ImmutableDictionary<string, SceneGroup>.Empty
                .Add("anchor", Scene.Group("support", [Scene.Box("column", new(.25f, 1.5f, .25f), new(.3f, .36f, .42f, 1), new(0, -.75f, 0))]))
                .Add("deck", Scene.Group("deck", [Scene.Box("top", new(4, .3f, 3), new(.12f, .6f, .8f, 1)),
                    Scene.Box("stripe", new(3.6f, .02f, .16f), new(1, .68f, .12f, 1), new(0, .16f, 0))])),
        };
        scene.Spawn("left", definition, SceneTransform.At(new(-3, .75f, 0)));
        scene.Spawn("right", definition, SceneTransform.At(new(3, .75f, 0)));
        var characterDefinition = new PhysicsCharacterAgentDefinition3D(physics,
            Scene.Group("character",
            [
                Scene.Box("body", new(.5f, 1.2f, .4f), new(.95f, .45f, .12f, 1), new(0, .6f, 0)),
                Scene.Box("head", new(.45f), new(.95f, .75f, .4f, 1), new(0, 1.55f, 0)),
            ]), "proof/character/v1");
        var character = scene.Spawn("player", characterDefinition, SceneTransform.At(new(-2.2f, .9f, 0)));
        Require(character.State.SupportBodyId == "left/deck", "Spawn did not resolve its physical platform.");
        Vector3 localFeet = character.State.SupportLocalFeet;
        float maximumLocalDrift = 0;
        float highestFeet = character.State.Motion.Feet.Y;
        PhysicsCharacterState3D? jump = null;
        var samples = new List<double>();
        var states = new List<PhysicsCharacterState3D> { character.State };
        Vector3 eye = new(10, 8, 13);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(1.05f, width / (float)height, .1f, 100);
        projection.M22 *= -1;
        Matrix4x4 camera = Matrix4x4.CreateLookAt(eye, new(0, 1, 0), Vector3.UnitY) * projection;
        var clear = new NativeFrameClearColor(.025f, .035f, .055f, 1);
        byte[] initial = Capture("initial");
        for (int tick = 1; tick <= 180; tick++)
        {
            physics.SetMotion("left/anchor", PhysicsPose3D.At(new(-3 + .004f * (tick - 1), .75f + .001f * (tick - 1), 0)),
                new(new(.24f, .06f, 0), Vector3.Zero));
            long started = Stopwatch.GetTimestamp();
            physics.Step(new(tick));
            characterDefinition.Move(character, new(tick, Vector3.Zero, tick == 91));
            PhysicsScene3D.Publish(scene, physics);
            samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            highestFeet = Math.Max(highestFeet, character.State.Motion.Feet.Y);
            if (tick <= 90)
            {
                Require(character.State.Grounded && character.State.SupportBodyId == "left/deck", "Character lost its moving support.");
                maximumLocalDrift = Math.Max(maximumLocalDrift, Vector3.Distance(character.State.SupportLocalFeet, localFeet));
            }
            if (tick == 91)
            {
                jump = character.State;
                Require(!jump.Grounded && jump.SupportBodyId is null && jump.AirborneVelocity.Length() > .1f,
                    "Jump did not detach with inherited carrier velocity.");
            }
            if (tick is 60 or 91 or 115 or 180)
            {
                states.Add(character.State);
                Capture($"tick-{tick:D3}");
            }
        }
        byte[] final = Capture("final");
        Require(maximumLocalDrift < .01f, $"Platform-local foot drift was {maximumLocalDrift} m.");
        Require(highestFeet > 1.6f && jump is not null, "Jump did not visibly leave the platform.");
        Require(!initial.SequenceEqual(final), "The composed scene did not change its Vulkan output.");
        Require(physics.JointCount == 4, "Repeated assemblies lost their joints.");
        var json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
        File.WriteAllText(Path.Combine(output, "states.json"), JsonSerializer.Serialize(states, json));
        var compiled = assembly.Compile();
        // Serialize concrete joint payloads explicitly; the base contract alone would omit anchors and drives.
        var jointEvidence = compiled.Joints.Select(joint => joint switch
        {
            PhysicsJoint3D.Hinge hinge => new { Kind = "revolute", Description = (object)hinge },
            PhysicsJoint3D.AngularMotor drive => new { Kind = "angular-drive", Description = (object)drive },
            PhysicsJoint3D.Fixed weld => new { Kind = "fixed", Description = (object)weld },
            PhysicsJoint3D.BallSocket socket => new { Kind = "spherical", Description = (object)socket },
            _ => throw new NotSupportedException("Unsupported proof joint."),
        }).ToArray();
        File.WriteAllText(Path.Combine(output, "assembly.json"), JsonSerializer.Serialize(new
        {
            compiled.DefinitionId,
            compiled.Bodies,
            compiled.Ports,
            compiled.Connections,
            Joints = jointEvidence,
        }, json));
        double[] sorted = samples.Skip(30).Order().ToArray();
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            ValidationLayers = plant.Facts.EnabledValidationLayers,
            Tick = physics.Tick,
            Bodies = physics.BodyCount,
            Joints = physics.JointCount,
            MaximumLocalDriftMetres = maximumLocalDrift,
            HighestFeetMetres = highestFeet,
            StepCharacterPublishP95Milliseconds = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1],
            TimingPolicy = "Small native specimen; first 30 ticks excluded; rendering excluded; not a scaling benchmark.",
        }, json));
        Console.WriteLine($"AURELIAN_PHYSICS_COMPOSITION_PASSED {evidence}");

        byte[] Capture(string name)
        {
            long tickBefore = physics.Tick;
            var projected = SceneGeometry3D.BuildScene(scene.Project());
            for (int frame = 0; frame < 8; frame++) renderer.Render(projected, camera, eye, clear, false);
            byte[] pixels = renderer.Render(projected, camera, eye, clear, true).Pixels!;
            Require(physics.Tick == tickBefore, "Rendering advanced physics.");
            NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), width, height, pixels);
            return pixels;
        }
    }

    private static PhysicsAssembly3D Platform()
    {
        var frame = new PhysicsPose3D(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2));
        var ports = ImmutableDictionary<string, PhysicsPose3D>.Empty.Add("pivot", frame);
        return new("platform")
        {
            Parts =
            [
                new(new("anchor", new PhysicsShape3D.Box(new(.2f)), PhysicsPose3D.At(Vector3.Zero))
                {
                    MotionType = PhysicsMotionType3D.Kinematic,
                }) { Ports = ports },
                new(new("deck", new PhysicsShape3D.Box(new(4, .3f, 3)), PhysicsPose3D.At(Vector3.Zero))) { Ports = ports },
            ],
            Interfaces = [new PhysicsInterface3D<Revolute3D>("bearing", new("anchor", "pivot"), new("deck", "pivot"))
            {
                Drive = new(.6f, 500),
            }],
            Expose = ImmutableDictionary<string, PhysicsEndpoint3D>.Empty.Add("pivot", new("anchor", "pivot")),
        };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
