using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Aetheris.Cloth3D;
using Aurelian.Cloth3D.Vulkan;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;

internal static class ClothSystemsProof
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        Write(output, "evidence.json", new { Accepted = false });
        byte[] spirv = ClothCompute3D.Compile(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Cloth3D.v.ts")));
        File.WriteAllBytes(Path.Combine(output, "cloth.spv"), spirv);
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian cloth systems"));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        var bending = Bending(output);
        var layers = Verify(plant, spirv, Layers(), 30, new()
        {
            Substeps = 1, Iterations = 4, ContactIterations = 2, Gravity = Vector3.Zero,
        }, new(), snapshot => snapshot with
        {
            Velocities = Enumerable.Range(0, 8).Select(index => new Vector3(0, index < 4 ? -10 : 10, 0)).ToImmutableArray(),
        }, snapshot => Require(snapshot.Positions.Take(4).Min(position => position.Y)
            > snapshot.Positions.Skip(4).Max(position => position.Y), "Layers changed ordering."));
        Write(output, "layers.json", layers);
        var sphere = Verify(plant, spirv,
            ClothDefinition3D.Grid("sphere", 2, 2, new(.1f, .1f), new(-.05f, 1, -.05f)) with { OffsetContact = true },
            8, new() { Substeps = 1, Iterations = 4, Gravity = Vector3.Zero },
            new() { SphereCenter = Vector3.Zero, SphereRadius = .5f }, snapshot => snapshot with
            {
                Velocities = Enumerable.Repeat(new Vector3(0, -120, 0), 4).ToImmutableArray(),
            }, snapshot => Require(snapshot.Positions.All(position => position.Y > .5f), "Sphere tunneling returned."));
        Write(output, "sphere.json", sphere);
        var strips = Verify(plant, spirv, CrossingStrips(), 12,
            new() { Substeps = 1, Iterations = 4, ContactIterations = 2, Gravity = Vector3.Zero }, new(),
            snapshot => snapshot, snapshot => Require(snapshot.Positions.Take(3).Max(position => position.Y)
                < snapshot.Positions.Skip(3).Min(position => position.Y), "Crossing strips changed ordering."));
        Write(output, "edge-pairs.json", strips);
        var pinPlan = Layers() with { Pins = [0, 1, 2, 3] };
        using (var pins = new VulkanClothSolver3D(plant, pinPlan.Compile(), spirv))
        {
            foreach (int pin in pinPlan.Pins)
            {
                pins.SetPin(pin, pinPlan.Positions[pin] - Vector3.UnitY);
            }
            pins.Step(new() { Substeps = 1, Iterations = 4, Gravity = Vector3.Zero }, new());
            var snapshot = pins.Capture();
            var metrics = ClothState3D.Measure(pins.Plan, snapshot, new());
            Require(snapshot.Positions.Take(4).Min(position => position.Y)
                > snapshot.Positions.Skip(4).Max(position => position.Y), "Driven pins crossed the lower layer.");
            Require(metrics.MaximumPinError > .5f, "Bounded pin motion hid a missed target.");
            Write(output, "driven-pins.json", metrics);
        }
        var bendingSolver = Verify(plant, spirv,
            ClothDefinition3D.Grid("bending", 5, 5, new(1, 1), Vector3.Zero) with
            {
                Material = new() { BendCompliance = .0001f },
            }, 8, new() { Substeps = 2, Iterations = 8, Gravity = Vector3.Zero }, new(), snapshot => snapshot with
            {
                Positions = snapshot.Positions.Select(position => position with
                {
                    Y = .05f * MathF.Sin(position.X * 4) * MathF.Cos(position.Z * 3),
                }).ToImmutableArray(),
            }, _ => { });
        Write(output, "bending-solver.json", bendingSolver);
        var regionalDefinition = ClothDefinition3D.Grid("regional-compliance", 5, 5, new(1, 1), Vector3.Zero) with
        {
            Material = new() { BendCompliance = .0001f },
            MaterialRegions = [new("upper-band", Enumerable.Range(0, 15).ToImmutableArray(), .25f, 4)],
        };
        var regional = Verify(plant, spirv, regionalDefinition, 8,
            new() { Substeps = 2, Iterations = 8, Gravity = Vector3.Zero }, new(), snapshot => snapshot with
            {
                Positions = snapshot.Positions.Select(position => position with
                {
                    Y = .05f * MathF.Sin(position.X * 4) * MathF.Cos(position.Z * 3),
                }).ToImmutableArray(),
            }, _ => { });
        Write(output, "regional-compliance.json", regional);
        var quadDefinition = ClothQuadSurface3D.Grid("quad-membrane", 5, 5, new(1, 1), Vector3.Zero) with
        {
            Material = new() { WarpCompliance = .001f, WeftCompliance = .004f,
                DiagonalCompliance = .002f, BendCompliance = .01f },
        };
        ClothSnapshot3D DeformSurface(ClothSnapshot3D snapshot)
        {
            return snapshot with
            {
                Positions = snapshot.Positions.Select(position => new Vector3(position.X * 1.1f,
                    position.Y + .05f * MathF.Sin(position.X * 4) * MathF.Cos(position.Z * 3),
                    position.Z)).ToImmutableArray(),
            };
        }
        var quadMembrane = Verify(plant, spirv, quadDefinition, 8,
            new() { Substeps = 2, Iterations = 8, Gravity = Vector3.Zero }, new(), DeformSurface, _ => { });
        Write(output, "quad-membrane.json", quadMembrane);
        var curvedDefinition = quadDefinition with
        {
            Id = "quad-curved-rest",
            Positions = quadDefinition.Positions.Select(position => position with
            {
                Y = .1f * MathF.Sin(position.X * MathF.PI),
            }).ToImmutableArray(),
            Surface = quadDefinition.Surface! with { Rest = ClothSurfaceRest3D.FittedSurface },
        };
        var quadCurved = Verify(plant, spirv, curvedDefinition, 8,
            new() { Substeps = 2, Iterations = 8, Gravity = Vector3.Zero }, new(), DeformSurface, _ => { });
        Write(output, "quad-curved.json", quadCurved);
        var stitched = Layers() with
        {
            OffsetContact = false,
            InitialPositions = Layers().Positions.Select((position, index) =>
                index < 4 ? position + new Vector3(0, .1f, 0) : position).ToImmutableArray(),
            Stitches =
            [
                new("sample-a", [0, 1, 4, 5], [.3f, .7f, -.3f, -.7f]),
                new("sample-b", [2, 3, 6, 7], [.6f, .4f, -.6f, -.4f]),
            ],
        };
        var seams = Verify(plant, spirv, stitched, 16,
            new() { Gravity = Vector3.Zero, Substeps = 2, Iterations = 8, StitchComplianceScale = 10 },
            new(), snapshot => snapshot, snapshot =>
            {
                foreach (var stitch in stitched.Stitches)
                {
                    var gap = Vector3.Zero;
                    for (int i = 0; i < stitch.Vertices.Length; i++)
                    {
                        gap += snapshot.Positions[stitch.Vertices[i]] * stitch.Weights[i];
                    }
                    Require(gap.Length() < .001f, "Weighted GPU stitch did not close.");
                }
            });
        Write(output, "stitches.json", seams);
        float minimumDrapeHeight = 2.6f;
        var drape = Verify(plant, spirv,
            ClothDefinition3D.Grid("drape", 17, 17, new(3, 3), new(-1.5f, 2.6f, -1.5f)) with
            {
                Pins = [0, 16],
            }, 12, new(), new()
            {
                PlaneNormal = Vector3.UnitY,
                SphereCenter = new(0, 1, 0),
                SphereRadius = .7f,
            }, snapshot => snapshot, snapshot =>
            {
                Require(snapshot.Positions.All(position => float.IsFinite(position.X)
                    && float.IsFinite(position.Y) && float.IsFinite(position.Z)), "Drape produced nonfinite state.");
                minimumDrapeHeight = MathF.Min(minimumDrapeHeight,
                    snapshot.Positions.Skip(17).Min(position => position.Y));
            });
        Require(minimumDrapeHeight < 2.55f, "Default vertex bending prevented the drape from falling.");
        Write(output, "drape.json", drape);
        Write(output, "evidence.json", new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            Bending = bending,
            Layers = layers,
            Sphere = sphere,
            EdgePairs = strips,
            BendingSolver = bendingSolver,
            Drape = drape,
            WeightedStitches = seams,
            RegionalCompliance = regional,
            QuadMembrane = quadMembrane,
            QuadCurvedRest = quadCurved,
            AcceleratedBroadphaseQualified = false,
            FrictionQualified = false,
            AvbdClothBackendQualified = false,
            PhysicalMaterialCalibrationQualified = false,
        });
        Console.WriteLine("AURELIAN_CLOTH_SYSTEMS_PASSED");
    }

    private static List<object> Bending(string output)
    {
        var result = new List<object>();
        foreach (int rows in new[] { 9, 17, 33 })
        {
            foreach (int aspect in new[] { 1, 4 })
            {
                foreach (int degrees in new[] { 0, 45 })
                {
                    foreach (var model in Enum.GetValues<ClothBendingModel3D>())
                    {
                        var definition = ClothDefinition3D.Grid("cylinder", (rows - 1) * aspect + 1, rows,
                            new(MathF.PI, 1), Vector3.Zero) with { BendingModel = model };
                        var rotation = Matrix4x4.CreateRotationY(degrees * MathF.PI / 180);
                        definition = definition with
                        {
                            Positions = definition.Positions.Select(position => Vector3.Transform(position, rotation)).ToImmutableArray(),
                        };
                        var plan = definition.Compile();
                        using var solver = new ClothSolver3D(plan);
                        var rest = solver.Capture();
                        float restEnergy = ClothState3D.Measure(plan, rest, new()).BendEnergy;
                        var cylinder = rest with
                        {
                            Positions = rest.Positions.Select(position =>
                                new Vector3(MathF.Sin(position.X), 1 - MathF.Cos(position.X), position.Z)).ToImmutableArray(),
                        };
                        float energy = ClothState3D.Measure(plan, cylinder, new()).BendEnergy;
                        float ratio = energy / (MathF.PI / 2);
                        Require(float.IsFinite(energy) && restEnergy < .0001f, "Bending operator has a nonfinite or nonzero flat-rest energy.");
                        if (rows == 33 && model == ClothBendingModel3D.BoundaryCorrectedVertex)
                        {
                            Require(MathF.Abs(ratio - 1) < .15f, "Vertex bending missed the fine cylinder acceptance budget.");
                        }
                        result.Add(new { Rows = rows, Aspect = aspect, Degrees = degrees, Model = model.ToString(),
                            Energy = energy, Ratio = ratio, FlatRestEnergy = restEnergy });
                    }
                }
            }
        }
        Write(output, "bending.json", result);
        return result;
    }

    private static object Verify(AurelianVulkanPlant plant, byte[] spirv, ClothDefinition3D definition,
        int ticks, ClothStepOptions3D options, ClothContacts3D contacts,
        Func<ClothSnapshot3D, ClothSnapshot3D> initialize, Action<ClothSnapshot3D> assert)
    {
        var plan = definition.Compile();
        using var cpu = new ClothSolver3D(plan);
        using var gpu = new VulkanClothSolver3D(plant, plan, spirv);
        var initial = initialize(cpu.Capture());
        cpu.Restore(initial);
        gpu.Restore(initial);
        float maximumError = 0;
        float initialOverlap = ClothState3D.Measure(plan, initial, contacts).OffsetContact?.MaximumActiveOffsetOverlap ?? 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            cpu.Step(options, contacts);
            gpu.Step(options, contacts);
            var reference = cpu.Capture();
            var actual = gpu.Capture();
            assert(reference);
            assert(actual);
            float error = reference.Positions.Zip(actual.Positions, Vector3.Distance).Max();
            maximumError = MathF.Max(maximumError, error);
            Require(error < .001f, $"CPU/Vulkan cloth disagreement {error} m in {definition.Id}.");
        }
        var checkpoint = gpu.Capture();
        float finalOverlap = ClothState3D.Measure(plan, checkpoint, contacts).OffsetContact?.MaximumActiveOffsetOverlap ?? 0;
        if (initialOverlap > 0)
        {
            Require(finalOverlap < initialOverlap * .1f, "Offset normal response failed to separate the initially compressed features.");
        }
        gpu.Step(options, contacts);
        var expected = gpu.Capture();
        gpu.Restore(checkpoint);
        gpu.Step(options, contacts);
        var replay = gpu.Capture();
        Require(expected.Positions.SequenceEqual(replay.Positions)
            && expected.Velocities.SequenceEqual(replay.Velocities), "Contact rewind did not replay exactly.");
        return new
        {
            definition.Id, Ticks = ticks, Vertices = definition.Positions.Length,
            CandidatePairs = plan.ContactTopology?.Pairs.Length ?? 0,
            ContactColors = plan.ContactTopology?.Colors.Length ?? 0,
            MaximumCpuGpuPositionError = maximumError,
            SameDeviceReplayIdentical = true,
            InitialActiveOffsetOverlap = initialOverlap,
            FinalActiveOffsetOverlap = finalOverlap,
            Cpu = ClothState3D.Measure(plan, cpu.Capture(), contacts),
            Gpu = ClothState3D.Measure(plan, checkpoint, contacts),
        };
    }

    private static ClothDefinition3D Layers()
    {
        var first = ClothDefinition3D.Grid("first", 2, 2, new(1, 1), new(0, .05f, 0));
        var second = ClothDefinition3D.Grid("second", 2, 2, new(1, 1), Vector3.Zero);
        return new("layers", first.Positions.AddRange(second.Positions), first.Coordinates.AddRange(second.Coordinates),
            first.Indices.AddRange(second.Indices.Select(index => index + 4))) { OffsetContact = true };
    }

    private static ClothDefinition3D CrossingStrips()
    {
        return new("strips", [new(-1, 0, 0), new(1, 0, 0), new(1, 0, -.1f),
                new(0, .001f, -1), new(0, .001f, 1), new(.1f, .001f, 1)],
            [new(0, 0), new(2, 0), new(2, .1f), new(0, 0), new(2, 0), new(2, .1f)], [0, 1, 2, 3, 4, 5])
        {
            OffsetContact = true,
        };
    }

    private static void Write(string output, string name, object value)
    {
        File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value,
            new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
