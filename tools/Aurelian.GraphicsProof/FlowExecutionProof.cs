using System.Diagnostics;
using System.Text.Json;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Qualification;
using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

internal static class FlowExecutionProof
{
    public static void Run(string output, bool nestedExperiment = false)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "flow-evidence.json");
        File.WriteAllText(evidence, "{\"accepted\":false}");
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian execution FLOW proof"));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        var cases = new List<(string Source, float[] Input, float[] Expected)>
        {
            (Source: "FlowChildProof.v.ts", Input: new float[] { 0, 1, 2, 3, 4, 5 }, Expected: new float[] { 10, 11, 12, 1000, 104, 1000 }),
            (Source: "FlowNestedProof.v.ts", Input: new float[] { 0, 1, 2, 3, 4, 5 }, Expected: new float[] { 20, 21, 22, 23, 104, 25 }),
            (Source: "FlowReentryProof.v.ts", Input: new float[] { 0, 1, 2 }, Expected: new float[] { 0, 202, 404 }),
            (Source: "FlowReuseProof.v.ts", Input: new float[] { 0, 1, 2, 3, 4, 5 }, Expected: new float[] { 2, 3, 4, 5, 6, 7 }),
            (Source: "FlowCheckpointProof.v.ts", Input: new float[] { 0, 1, 2, 3, 4, 5 }, Expected: new float[] { 200020, 210021, 220022, 230023, 1040104, 250025 }),
            (Source: "FlowStaticReplayProof.v.ts", Input: new float[] { 0 }, Expected: new float[] { 66 }),
        };
        if (nestedExperiment)
        {
            cases.Add(("FlowReplayProof.v.ts", [0], [66]));
        }
        var results = new List<object>();
        foreach (var specimen in cases)
        {
            var sources = GpuSourceLoader.Load(specimen.Source, name =>
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            });
            var module = GpuComputeBinder.Compile(new(sources));
            Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Code + ": " + item.Message)));
            string name = Path.GetFileNameWithoutExtension(specimen.Source);
            File.WriteAllText(Path.Combine(output, name + ".vdmir.json"), VdMirJson.Serialize(module));
            File.WriteAllText(Path.Combine(output, name + ".hlsl"), VdMirComputeHlslEmitter.Emit(module));
            var compilation = Stopwatch.StartNew();
            var backend = VdMirComputeBackend.Compile(module);
            compilation.Stop();
            if (!backend.SpirvValidated)
            {
                File.WriteAllText(evidence, JsonSerializer.Serialize(new
                {
                    Accepted = false,
                    Cases = results,
                    FailedSource = specimen.Source,
                    backend.DxcStatus,
                    backend.DxcOutput,
                    backend.SpirvValidationOutput,
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Require(backend.SpirvValidated, backend.DxcOutput + backend.SpirvValidationOutput);
            File.WriteAllBytes(Path.Combine(output, name + ".spv"), backend.Spirv);
            using var probe = new VulkanScalarComputeProbe(plant, backend.Spirv, module.EntryPoint!.EmittedName, specimen.Input);
            float[] actual = probe.Execute();
            float[] repeat = probe.Execute();
            results.Add(new
            {
                specimen.Source,
                specimen.Input,
                specimen.Expected,
                Actual = actual,
                RepeatIdentical = actual.SequenceEqual(repeat),
                BackendCompileMilliseconds = compilation.Elapsed.TotalMilliseconds,
                RootFrameBytes = module.ValueTypes?.FirstOrDefault(type => type.Name.EndsWith("_RootFrame", StringComparison.Ordinal))?.Size,
            });
            File.WriteAllText(evidence, JsonSerializer.Serialize(new { Accepted = false, Cases = results }, new JsonSerializerOptions { WriteIndented = true }));
            Require(actual.SequenceEqual(specimen.Expected), "Execution FLOW differs in " + specimen.Source + ": " + string.Join(", ", actual));
            Require(actual.SequenceEqual(repeat), "Repeated Vulkan execution changed the results.");
        }
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            Cases = results,
            SpirvValidated = true,
            ClosedNestedReplayEvaluatedAtCompileTime = true,
            TwoNestedChildrenActivated = true,
            TwoNestedChildrenCompletedOnGpu = true,
            YieldingChild = true,
            TypedFailure = true,
            RuntimeStack = false,
            ChildReentryResetsBoard = true,
            ReusedFragmentsDeliverTypedRecords = true,
            LocalCheckpointCompletionReplay = true,
            LocalValueReplay = nestedExperiment,
            PersistentBufferFrameRestoreQualified = false,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AURELIAN_VTS_FLOW_PROOF_PASSED " + plant.Facts.PhysicalDeviceName);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
