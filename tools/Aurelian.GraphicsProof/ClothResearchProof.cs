using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Aurelian.Cloth3D;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Qualification;
using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

/// <summary>Bounded mechanism/verification probes, not complete OGC or AVBD cloth solvers.</summary>
internal static class ClothResearchProof
{
    private const double Inertia = 3600;
    private const double GroundStiffness = 10000;
    private const double PenaltyReferenceStiffness = 100000000;
    private const double Prediction = .01;

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        var bending = BendingRefinement();
        Write(output, "bending.json", bending);
        Require(bending.Count == 12 && bending.All(sample => sample.Ratio.HasValue && sample.Diagnostic is null),
            "A bending refinement pattern failed to compile; inspect bending.json for its diagnostic.");
        var folding = SharpFold();
        Write(output, "folding.json", folding);
        var contact = ContactBounds();
        Write(output, "contact.json", contact);
        var convergence = Convergence();
        Write(output, "convergence.json", convergence);
        var native = NativeConvergence(output, convergence);
        Write(output, "evidence.json", new
        {
            Accepted = true,
            Bending = bending,
            SharpFold = folding,
            Contact = contact,
            Convergence = convergence,
            Native = native,
            FullOgcContactQualified = false,
            AvbdClothBackendQualified = false,
            PhysicalBendingCalibrationQualified = false,
        });
        Console.WriteLine("AURELIAN_CLOTH_RESEARCH_PROBES_PASSED");
    }

    private sealed record BendingSample(int Columns, int Rows, int RotationDegrees,
        string Mesh, float? GeometricEnergy, double AnalyticEnergy, double? Ratio, string? Diagnostic);

    private static List<BendingSample> BendingRefinement()
    {
        var results = new List<BendingSample>();
        foreach (int cells in new[] { 8, 16, 32 })
        {
            foreach (int aspect in new[] { 1, 4 })
            {
                foreach (int degrees in new[] { 0, 45 })
                {
                    int columns = cells * aspect + 1;
                    int rows = cells + 1;
                    // Rest area pi, unit-radius cylinder, normalized density 1/2 |curvature|^2.
                    // This is a geometric comparison, not the paper's material values in joules.
                    double analytic = Math.PI / 2;
                    var definition = ClothDefinition3D.Grid("cylinder", columns, rows,
                        new(MathF.PI, 1), Vector3.Zero) with { BendingModel = ClothBendingModel3D.QuadraticHinge };
                    var rotation = Matrix4x4.CreateRotationY(degrees * MathF.PI / 180);
                    definition = definition with
                    {
                        Positions = definition.Positions.Select(position => Vector3.Transform(position, rotation)).ToImmutableArray(),
                    };
                    try
                    {
                        var plan = definition.Compile();
                        using var solver = new ClothSolver3D(plan);
                        var cylinder = solver.Capture() with
                        {
                            Positions = definition.Positions.Select(position =>
                                new Vector3(MathF.Sin(position.X), 1 - MathF.Cos(position.X), position.Z)).ToImmutableArray(),
                        };
                        float energy = ClothState3D.Measure(plan, cylinder, new()).BendEnergy;
                        Require(float.IsFinite(energy), "Nonfinite cylinder energy.");
                        results.Add(new(columns, rows, degrees, aspect == 1 ? "regular" : "4x-anisotropic",
                            energy, analytic, energy / analytic, null));
                    }
                    catch (NotSupportedException exception)
                    {
                        // Preserve a real authoring stop rather than silently adjusting the mesh.
                        results.Add(new(columns, rows, degrees, aspect == 1 ? "regular" : "4x-anisotropic",
                            null, analytic, null, exception.Message));
                    }
                }
            }
        }
        return results;
    }

    private sealed record FoldSample(int Degrees, double Energy, double Moment);

    private static List<FoldSample> SharpFold()
    {
        var plan = (ClothDefinition3D.Grid("hinge", 2, 2, new(1, 1), Vector3.Zero) with
        {
            BendingModel = ClothBendingModel3D.QuadraticHinge,
        }).Compile();
        var hinge = plan.Constraints.Single(constraint => constraint.Kind == ClothConstraintKind3D.FlatBend);
        Vector3 origin = plan.Definition.Positions[hinge.Vertices[0]];
        Vector3 axis = Vector3.Normalize(plan.Definition.Positions[hinge.Vertices[1]] - origin);
        using var solver = new ClothSolver3D(plan);
        var rest = solver.Capture();
        var results = new List<FoldSample>();
        foreach (int degrees in new[] { 0, 30, 60, 90, 120, 150, 170, 179 })
        {
            double angle = degrees * Math.PI / 180;
            const double delta = .001;
            double moment = (Energy(angle + delta) - Energy(angle - delta)) / (2 * delta);
            results.Add(new(degrees, Energy(angle), moment));
        }
        Require(results.All(sample => double.IsFinite(sample.Energy) && double.IsFinite(sample.Moment)), "Nonfinite fold probe.");
        return results;

        double Energy(double angle)
        {
            int opposite = hinge.Vertices[3];
            var rotation = Matrix4x4.CreateFromAxisAngle(axis, (float)angle);
            Vector3 rotated = origin + Vector3.Transform(rest.Positions[opposite] - origin, rotation);
            var snapshot = rest with { Positions = rest.Positions.SetItem(opposite, rotated) };
            return ClothState3D.Measure(plan, snapshot, new()).BendEnergy;
        }
    }

    private sealed record ContactSample(double InitialGap, double Gamma, double ProposedTravel,
        double Bound, double AcceptedGap, bool FullMotionCrosses, bool BoundedMotionPreservesOrdering);

    private static List<ContactSample> ContactBounds()
    {
        var results = new List<ContactSample>();
        foreach (double gap in new[] { .0001, .01, .1 })
        {
            foreach (double gamma in new[] { .1, .4, .49 })
            {
                // Two congruent, separated triangles. All three OGC Eq.21 minima equal gap:
                // vertex/facet, incident facet/vertex, and incident edge/nonincident edge.
                // Uniform normal translations give an exact swept-ordering oracle.
                double proposed = gap * 100;
                double bound = gamma * gap;
                double accepted = Math.Min(proposed, bound);
                double finalGap = gap - 2 * accepted;
                results.Add(new(gap, gamma, proposed, bound, finalGap,
                    proposed * 2 > gap, finalGap > 0));
            }
        }
        Require(results.All(sample => sample.FullMotionCrosses && sample.BoundedMotionPreservesOrdering),
            "Conservative parallel-facet mechanism failed.");
        return results;
    }

    private sealed record ConvergenceSample(string Method, int Iterations, double X1, double X2,
        double Lambda, double PositionError, double ConstraintError, double StationarityResidual);

    private static List<ConvergenceSample> Convergence()
    {
        // Same inertia/load/ground spring. Target: hard coupling x1=x2.
        // VBD approximates the hard coupling with a finite 1e8 penalty; AVBD uses a dual variable.
        // The finite reference is explicitly a surrogate, not an identical-energy XPBD comparison.
        double exact = 2 * Inertia * Prediction / (2 * Inertia + GroundStiffness);
        var results = new List<ConvergenceSample>();
        foreach (string method in new[] { "VBD-finite-penalty", "AVBD-hard-coupling", "XPBD-hard-coupling" })
        {
            foreach (int iterations in new[] { 1, 5, 20, 64 })
            {
                double x1 = Prediction;
                double x2 = Prediction;
                double lambda = 0;
                double groundMultiplier = 0;
                double couplingMultiplier = 0;
                double penalty = Inertia;
                bool augmented = method == "AVBD-hard-coupling";
                bool xpbd = method == "XPBD-hard-coupling";
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    if (xpbd)
                    {
                        double alpha = Inertia / GroundStiffness;
                        double groundIncrement = (-x1 - alpha * groundMultiplier) / (1 + alpha);
                        groundMultiplier += groundIncrement;
                        x1 += groundIncrement;
                        double couplingIncrement = (x2 - x1) / 2;
                        couplingMultiplier += couplingIncrement;
                        x1 += couplingIncrement;
                        x2 -= couplingIncrement;
                        lambda = -Inertia * couplingMultiplier;
                        continue;
                    }
                    if (!augmented)
                    {
                        penalty = PenaltyReferenceStiffness;
                    }
                    x1 = (Inertia * Prediction + penalty * x2 - lambda) / (Inertia + GroundStiffness + penalty);
                    x2 = (Inertia * Prediction + penalty * x1 + lambda) / (Inertia + penalty);
                    if (augmented)
                    {
                        double residual = x1 - x2;
                        lambda += penalty * residual;
                        penalty += 10 * Math.Abs(residual); // AVBD Eq.12, beta=10.
                    }
                }
                double force = augmented || xpbd ? lambda : penalty * (x1 - x2);
                double residual1 = Inertia * (x1 - Prediction) + GroundStiffness * x1 + force;
                double residual2 = Inertia * (x2 - Prediction) - force;
                results.Add(new(method, iterations, x1, x2, lambda,
                    Math.Max(Math.Abs(x1 - exact), Math.Abs(x2 - exact)), Math.Abs(x1 - x2),
                    Math.Max(Math.Abs(residual1), Math.Abs(residual2))));
            }
        }
        Require(results.All(sample => double.IsFinite(sample.StationarityResidual)), "Nonfinite primal/dual solve.");
        Require(results.Single(sample => sample.Method == "AVBD-hard-coupling" && sample.Iterations == 64).PositionError < 1e-10,
            "Augmented solve did not approach its exact constrained solution.");
        Require(results.Single(sample => sample.Method == "XPBD-hard-coupling" && sample.Iterations == 64).PositionError < 1e-10,
            "XPBD control did not approach the same exact constrained solution.");
        return results;
    }

    private static object NativeConvergence(string output, List<ConvergenceSample> expected)
    {
        const string sourceName = "ClothAvbdProbe.v.ts";
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", sourceName));
        var module = GpuComputeBinder.Compile(new([new(sourceName, source)]));
        Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        File.WriteAllText(Path.Combine(output, "avbd.vdmir.json"), VdMirJson.Serialize(module));
        File.WriteAllText(Path.Combine(output, "avbd.hlsl"), VdMirComputeHlslEmitter.Emit(module));
        var backend = VdMirComputeBackend.Compile(module);
        Require(backend.SpirvValidated, backend.DxcOutput + backend.SpirvValidationOutput);
        File.WriteAllBytes(Path.Combine(output, "avbd.spv"), backend.Spirv);
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian cloth research probe"));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        float[] input = expected.SelectMany(sample => new[]
        {
            MethodTag(sample.Method),
            (float)sample.Iterations,
            0f,
        }).ToArray();
        using var probe = new VulkanScalarComputeProbe(plant, backend.Spirv, module.EntryPoint!.EmittedName, input);
        float[] actual = probe.Execute();
        float[] repeat = probe.Execute();
        Write(output, "avbd-native-values.json", actual);
        double positionError = 0;
        double lambdaError = 0;
        for (int index = 0; index < expected.Count; index++)
        {
            positionError = Math.Max(positionError, Math.Abs(actual[index * 3] - expected[index].X1));
            positionError = Math.Max(positionError, Math.Abs(actual[index * 3 + 1] - expected[index].X2));
            lambdaError = Math.Max(lambdaError, Math.Abs(actual[index * 3 + 2] - expected[index].Lambda));
        }
        Require(positionError < 1e-7 && lambdaError < .0005, "Native scalar probe differs from the double reference.");
        Require(actual.SequenceEqual(repeat), "Native scalar probe changed on repeated execution.");
        return new
        {
            Device = plant.Facts.PhysicalDeviceName,
            SpirvValidated = true,
            Cases = expected.Count,
            MaximumPositionDifference = positionError,
            MaximumDualDifference = lambdaError,
            RepeatIdentical = true,
            FullClothPerformanceQualified = false,
        };
    }

    private static float MethodTag(string method)
    {
        return method switch
        {
            "VBD-finite-penalty" => 0,
            "AVBD-hard-coupling" => 1,
            "XPBD-hard-coupling" => 2,
            _ => throw new ArgumentException("Unknown solver probe method."),
        };
    }

    private static void Write(string output, string file, object value)
    {
        File.WriteAllText(Path.Combine(output, file), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
