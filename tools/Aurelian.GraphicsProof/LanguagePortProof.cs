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
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "language-evidence.json");
        File.WriteAllText(evidencePath, "{\"accepted\":false}");
        var compute = GpuComputeBinder.Compile(new(Sources("LanguagePortCompute.v.ts")));
        Require(compute.Success, Diagnostics(compute.Diagnostics));
        var backend = VdMirComputeBackend.Compile(compute);
        Require(backend.SpirvValidated && backend.Spirv.Length > 0, backend.DxcOutput + backend.SpirvValidationOutput);
        Require(!compute.Functions.Any(function => function.Name.EndsWith("_Square", StringComparison.Ordinal)), "Static-only helper survived runtime closure.");
        File.WriteAllText(Path.Combine(output, "language-compute.vdmir.json"), VdMirJson.Serialize(compute));
        File.WriteAllText(Path.Combine(output, "language-compute.hlsl"), backend.Hlsl);
        File.WriteAllBytes(Path.Combine(output, "language-compute.spv"), backend.Spirv);

        var graphics = GpuGraphicsBinder.Compile(new(Sources("LanguagePortGraphics.v.ts")));
        Require(graphics.Success, Diagnostics(graphics.Diagnostics));
        var graphicsBackend = VdMirGraphicsBackend.Compile(graphics, "vulkan1.2");
        Require(graphicsBackend.Vertex.SpirvValidated && graphicsBackend.Pixel.SpirvValidated,
            graphicsBackend.Vertex.DxcOutput + graphicsBackend.Pixel.DxcOutput);
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
        float[] expected = [0, 0, .25f, 1, 4];
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
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_VTS_LANGUAGE_PROOF_PASSED " + plant.Facts.PhysicalDeviceName);
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
