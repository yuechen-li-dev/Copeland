using System.Diagnostics;
using System.Text.Json;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Qualification;
using Aurelian.Shaders.Compute;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Gpu.Wgsl;

internal static class LanguagePortProof
{
    public static void Run(string output, bool shapes = false, bool generics = false)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "language-evidence.json");
        File.WriteAllText(evidencePath, "{\"accepted\":false}");
        string computeSource = generics ? "GenericCompute.v.ts" : shapes ? "ShapeCompute.v.ts" : "LanguagePortCompute.v.ts";
        var compute = GpuComputeBinder.Compile(new(Sources(computeSource)));
        Require(compute.Success, Diagnostics(compute.Diagnostics));
        var backend = VdMirComputeBackend.Compile(compute);
        Require(backend.SpirvValidated && backend.Spirv.Length > 0, backend.DxcOutput + backend.SpirvValidationOutput);
        Require(!compute.Functions.Any(function => function.Name.EndsWith("_Square", StringComparison.Ordinal)), "Static-only helper survived runtime closure.");
        if (generics)
        {
            var specializations = compute.GenericSpecializations!;
            var staticOnly = specializations.Where(item => item.Declaration.EndsWith("_Build", StringComparison.Ordinal)
                || item.Declaration.EndsWith("_Samples", StringComparison.Ordinal)).ToArray();
            Require(staticOnly.Length == 2, "Both aggregate construction specializations must be traced.");
            Require(!compute.Functions.Any(function => staticOnly.Any(item => item.Function == function.Name)),
                "Compile-time aggregate constructors survived runtime closure.");
            Require(specializations.All(item => item.BodyBindings == 1), "An open body was rebound during specialization.");
        }
        File.WriteAllText(Path.Combine(output, "language-compute.vdmir.json"), VdMirJson.Serialize(compute));
        File.WriteAllText(Path.Combine(output, "language-compute.hlsl"), backend.Hlsl);
        File.WriteAllBytes(Path.Combine(output, "language-compute.spv"), backend.Spirv);

        string graphicsSource = generics ? "GenericGraphics.v.ts" : shapes ? "ShapeGraphics.v.ts" : "LanguagePortGraphics.v.ts";
        var graphics = GpuGraphicsBinder.Compile(new(Sources(graphicsSource)));
        Require(graphics.Success, Diagnostics(graphics.Diagnostics));
        var graphicsBackend = VdMirGraphicsBackend.Compile(graphics, "vulkan1.2");
        Require(graphicsBackend.Vertex.SpirvValidated && graphicsBackend.Pixel.SpirvValidated,
            graphicsBackend.Vertex.DxcOutput + graphicsBackend.Pixel.DxcOutput);
        File.WriteAllText(Path.Combine(output, "language-graphics.vdmir.json"), VdMirJson.Serialize(graphics));
        File.WriteAllText(Path.Combine(output, "language-graphics.hlsl"), graphicsBackend.Hlsl);
        File.WriteAllBytes(Path.Combine(output, "language-graphics.spv"), graphicsBackend.Pixel.Spirv);
        var wgsl = WgslGraphicsBackend.Lower(graphics);
        Require(wgsl.Success, Diagnostics(wgsl.Diagnostics));
        string wgslPath = Path.Combine(output, "language-graphics.wgsl");
        File.WriteAllText(wgslPath, wgsl.Program!.Code);
        ValidateWgsl(wgslPath);

        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian VTS Language Proof"));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        float[] input = [-4, 0, 1, 4, 16];
        float[] expected = generics ? [7, 23, 27, 39, 87] : shapes ? [34, 58, 62, 107, 467] : [0, 0, .25f, 1, 4];
        using var kernel = new VulkanScalarComputeProbe(plant, backend.Spirv, compute.EntryPoint!.EmittedName, input);
        float[] actual = kernel.Execute();
        Require(actual.SequenceEqual(expected), "GPU payload/static results differ: " + string.Join(", ", actual));
        float[] repeat = kernel.Execute();
        Require(actual.SequenceEqual(repeat), "Repeated compute dispatch changed values.");
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            Input = input,
            Expected = expected,
            Actual = actual,
            RepeatIdentical = true,
            ComputeSpirvValidated = true,
            GraphicsSpirvValidated = true,
            DirectWgslValidated = true,
            StaticOnlyHelperErased = true,
            FixedShapesAndRecords = shapes || generics,
            GenericSpecializations = generics ? compute.GenericSpecializations : null,
        }, new JsonSerializerOptions { WriteIndented = true }));
        string label = generics ? "AURELIAN_VTS_GENERIC_PROOF_PASSED " : shapes ? "AURELIAN_VTS_SHAPE_PROOF_PASSED " : "AURELIAN_VTS_LANGUAGE_PROOF_PASSED ";
        Console.WriteLine(label + plant.Facts.PhysicalDeviceName);
    }

    private static IReadOnlyList<GpuSourceFile> Sources(string root)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Assets");
        return GpuSourceLoader.Load(root, name =>
        {
            string path = Path.Combine(directory, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        });
    }

    private static void ValidateWgsl(string path)
    {
        string executable = Environment.GetEnvironmentVariable("AURELIAN_NAGA")
            ?? Path.GetFullPath("artifacts/aurelian-beacon3d/toolchain/bin/naga.exe");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            },
        };
        process.StartInfo.ArgumentList.Add(path);
        process.Start();
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Require(process.ExitCode == 0, "WGSL validation failed: " + stdout + stderr);
    }

    private static string Diagnostics(IEnumerable<VdMirDiagnostic> diagnostics)
        => string.Join("; ", diagnostics.Select(item => item.Message));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
