using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Aetheris.Cloth3D;
using Aurelian.Cloth3D.Vulkan;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Qualification;
using Aurelian.Shaders.Compute;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;

internal static class ComputeRefactorProof
{
    private sealed record Kernel([property: System.Text.Json.Serialization.JsonIgnore] byte[] Spirv, string Entry, double CompileMilliseconds,
        int SourceBytes, int HlslBytes, int SpirvBytes, string SourceSha256);
    private sealed record Timing(double MedianMilliseconds, double P95Milliseconds);

    public static void Run(string output, string baselineDirectory)
    {
        Directory.CreateDirectory(output);
        Write(output, "evidence.json", new { Accepted = false });
        Kernel cloth = Compile(output, "cloth", Path.Combine(AppContext.BaseDirectory, "Assets", "Cloth3D.v.ts"));
        Kernel skin = Compile(output, "skinning", Path.Combine(AppContext.BaseDirectory, "Assets", "HumanoidSkinning.v.ts"));
        Kernel oldCloth = Compile(output, "cloth-baseline", Path.Combine(baselineDirectory, "Cloth3D.baseline.v.ts"));
        Kernel oldSkin = Compile(output, "skinning-baseline", Path.Combine(baselineDirectory, "HumanoidSkinning.baseline.v.ts"));
        Kernel storage = Compile(output, "storage", Path.Combine(AppContext.BaseDirectory, "Assets", "ComputeStorageProof.v.ts"));
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian compute value qualification"));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        var storageResult = Storage(plant, storage);
        var skinningResult = Skinning(plant, oldSkin, skin);
        var clothResult = Cloth(plant, oldCloth, cloth);
        Write(output, "evidence.json", new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            Storage = storageResult,
            Skinning = skinningResult,
            Cloth = clothResult,
            Compilation = new { Cloth = cloth, Skinning = skin, OldCloth = oldCloth, OldSkinning = oldSkin },
            TimingScope = "Host upload, command recording, submission and completion; excludes readback and restore. Not GPU timestamps.",
        });
        Console.WriteLine("AURELIAN_COMPUTE_REFACTOR_PASSED " + plant.Facts.PhysicalDeviceName);
    }

    private static Kernel Compile(string output, string label, string path)
    {
        string source = File.ReadAllText(path);
        string authoredSource = source;
        if (label.EndsWith("-baseline", StringComparison.Ordinal))
        {
            // The saved scalar shaders predate compiler-owned vector intrinsics.
            // Rename their helpers only; keep the original arithmetic and transport.
            source = System.Text.RegularExpressions.Regex.Replace(source, @"\b(Dot|Cross|Length|Normalize)\b", "Legacy$1");
        }
        var timer = Stopwatch.StartNew();
        var module = GpuComputeBinder.Compile(new([new(Path.GetFileName(path), source)]));
        Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        var backend = VdMirComputeBackend.Compile(module);
        timer.Stop();
        File.WriteAllText(Path.Combine(output, label + ".hlsl"), backend.Hlsl);
        Require(backend.SpirvValidated, backend.DxcOutput + backend.SpirvValidationOutput);
        File.WriteAllBytes(Path.Combine(output, label + ".spv"), backend.Spirv);
        File.WriteAllText(Path.Combine(output, label + ".vdmir.json"), VdMirJson.Serialize(module));
        foreach (var layout in module.StorageLayouts ?? [])
        {
            File.WriteAllText(Path.Combine(output, label + "." + layout.Type + ".g.cs"),
                GpuStoragePacking.EmitCSharp(layout, "Aurelian.Generated", layout.Type + "Packing"));
        }
        // Binary contents are saved separately; keep the evidence JSON compact.
        return new(backend.Spirv, module.EntryPoint!.EmittedName, timer.Elapsed.TotalMilliseconds,
            System.Text.Encoding.UTF8.GetByteCount(authoredSource), System.Text.Encoding.UTF8.GetByteCount(backend.Hlsl),
            backend.Spirv.Length, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(authoredSource))));
    }

    private static object Storage(AurelianVulkanPlant plant, Kernel kernel)
    {
        float[] input = [10, 20, 30, BitConverter.Int32BitsToSingle(unchecked((int)0xf1234567)), 0, 0, 0, 0, 0, 0, 0, 0];
        using var probe = new VulkanScalarComputeProbe(plant, kernel.Spirv, kernel.Entry, input);
        float[] output = probe.Execute();
        uint[] bits = output.Select(value => unchecked((uint)BitConverter.SingleToInt32Bits(value))).ToArray();
        Require(output[0] == 11 && output[1] == 22 && output[2] == 33, "Typed vector/payload transfer failed.");
        Require(bits[3] == 0xf1234567 && bits[5] == 0xf1234567, "u32 transport lost integer bits.");
        Require(bits[4] == 4 && bits[6] == 0 && bits[7] == 0 && output[8] == 10 && bits[9] == 0,
            "Word layout, mixed scalar transfer or checked loads failed.");
        Require(bits[10] == 1 && bits[11] == 99, "Match evaluated its subject twice or out-of-bounds stores wrote data.");
        Require(output.SequenceEqual(probe.Execute()), "Repeated typed storage dispatch changed output.");
        return new { IntegerBitsPreserved = true, OverflowLoadsZero = true, OverflowStoresNoOp = true, SubjectEvaluatedOnce = true };
    }

    private static object Skinning(AurelianVulkanPlant plant, Kernel baseline, Kernel current)
    {
        const int vertices = 30_000;
        float[] rest = new float[vertices * 24];
        for (int vertex = 0; vertex < vertices; vertex++)
        {
            int offset = vertex * 24;
            rest[offset] = vertex % 101 - 50;
            rest[offset + 1] = vertex % 37 - 18;
            rest[offset + 2] = 500 + vertex % 997;
            rest[offset + 5] = 1;
            for (int influence = 0; influence < 6; influence++)
            {
                rest[offset + 6 + influence * 2] = influence;
                rest[offset + 7 + influence * 2] = (influence + 1) / 21f;
            }
            rest[offset + 18] = 8;
            rest[offset + 19] = -3;
            rest[offset + 22] = 5;
            rest[offset + 23] = -2;
        }
        using var oldSkin = new VulkanSkinning3D(plant, baseline.Spirv, rest, vertices, 6);
        using var skin = new VulkanSkinning3D(plant, current.Spirv, rest, vertices, 6);
        float positionError = 0;
        float normalError = 0;
        for (int pose = 0; pose < 12; pose++)
        {
            float[] palette = Palette(pose);
            float left = pose / 11f;
            float right = 1 - left;
            var world = Matrix4x4.CreateRotationY(pose * .07f) * Matrix4x4.CreateTranslation(1, 2, 3);
            oldSkin.Deform(palette, left, right, world);
            skin.Deform(palette, left, right, world);
            Native3DVertex[] expected = oldSkin.ReadVertices();
            Native3DVertex[] actual = skin.ReadVertices();
            for (int vertex = 0; vertex < vertices; vertex++)
            {
                float position = Vector3.Distance(expected[vertex].Position, actual[vertex].Position);
                float normal = Vector3.Distance(expected[vertex].Normal, actual[vertex].Normal);
                Require(float.IsFinite(position) && float.IsFinite(normal), "Skinning produced nonfinite output.");
                positionError = MathF.Max(positionError, position);
                normalError = MathF.Max(normalError, normal);
                Require(expected[vertex].Color == actual[vertex].Color, "Skinning changed presentation color.");
                if (pose == 0)
                {
                    int offset = vertex * 24;
                    Vector3 corrected = new(rest[offset], rest[offset + 1] + 5, rest[offset + 2] - 2);
                    Vector3 identity = new(corrected.X * .001f + 1, corrected.Z * .001f + 2, -corrected.Y * .001f + 3);
                    Require(Vector3.Distance(identity, actual[vertex].Position) < .00001f, "Skinning failed the independent identity/corrective oracle.");
                }
            }
        }
        Require(positionError < .00001f && normalError < .00001f, "Refactored skinning exceeded its numerical budget.");
        float[] timedPalette = Palette(8);
        var timing = Compare(() => oldSkin.Deform(timedPalette, .4f, .6f, Matrix4x4.Identity),
            () => skin.Deform(timedPalette, .4f, .6f, Matrix4x4.Identity));
        return new { Vertices = vertices, Poses = 12, MaximumPositionErrorMetres = positionError,
            MaximumNormalError = normalError, IdentityOraclePassed = true, Baseline = timing.Old, Refactored = timing.Current };
    }

    private static float[] Palette(int pose)
    {
        var palette = new float[48];
        for (int joint = 0; joint < 6; joint++)
        {
            Quaternion real = Quaternion.CreateFromYawPitchRoll(pose * .015f * joint, pose * .01f, -pose * .02f * joint);
            Quaternion dual = Quaternion.Multiply(new Quaternion(pose * joint, -pose, pose * 2, 0), real) * .5f;
            float sign = joint % 2 == 0 ? 1 : -1;
            int offset = joint * 8;
            palette[offset] = real.X * sign;
            palette[offset + 1] = real.Y * sign;
            palette[offset + 2] = real.Z * sign;
            palette[offset + 3] = real.W * sign;
            palette[offset + 4] = dual.X * sign;
            palette[offset + 5] = dual.Y * sign;
            palette[offset + 6] = dual.Z * sign;
            palette[offset + 7] = dual.W * sign;
        }
        return palette;
    }

    private static object Cloth(AurelianVulkanPlant plant, Kernel baseline, Kernel current)
    {
        var definition = ClothDefinition3D.Grid("compute-refactor", 17, 17, new(2, 2), new(-1, 1, -1)) with { Pins = [0, 16] };
        var plan = definition.Compile();
        using var cpu = new ClothSolver3D(plan);
        using var oldCloth = new VulkanClothSolver3D(plant, plan, baseline.Spirv);
        using var cloth = new VulkanClothSolver3D(plant, plan, current.Spirv);
        var options = new ClothStepOptions3D { Substeps = 2, Iterations = 4, ContactIterations = 1 };
        var contacts = new ClothContacts3D { PlaneNormal = Vector3.UnitY };
        float baselineError = 0;
        float cpuError = 0;
        for (int tick = 0; tick < 24; tick++)
        {
            cpu.Step(options, contacts);
            oldCloth.Step(options, contacts);
            cloth.Step(options, contacts);
            var actual = cloth.Capture();
            float delta = actual.Positions.Zip(oldCloth.Capture().Positions, Vector3.Distance).Max();
            float reference = actual.Positions.Zip(cpu.Capture().Positions, Vector3.Distance).Max();
            Require(float.IsFinite(delta) && float.IsFinite(reference), "Cloth produced nonfinite output.");
            baselineError = MathF.Max(baselineError, delta);
            cpuError = MathF.Max(cpuError, reference);
        }
        Require(baselineError < .001f && cpuError < .001f, "Refactored cloth exceeded its millimetre budget.");
        var checkpoint = cloth.Capture();
        cloth.Step(options, contacts);
        var expected = cloth.Capture();
        cloth.Restore(checkpoint);
        cloth.Step(options, contacts);
        var replay = cloth.Capture();
        Require(expected.Positions.SequenceEqual(replay.Positions) && expected.Velocities.SequenceEqual(replay.Velocities),
            "Refactored cloth rewind failed exact replay.");
        var oldCheckpoint = oldCloth.Capture();
        var timing = Compare(() => oldCloth.Step(options, contacts), () => cloth.Step(options, contacts),
            () => oldCloth.Restore(oldCheckpoint), () => cloth.Restore(checkpoint));
        return new { Vertices = 289, Ticks = 24, MaximumBaselineErrorMetres = baselineError,
            MaximumCpuErrorMetres = cpuError, ExactRewindReplay = true, Baseline = timing.Old, Refactored = timing.Current };
    }

    private static (Timing Old, Timing Current) Compare(Action baseline, Action current, Action? resetBaseline = null, Action? resetCurrent = null)
    {
        var oldSamples = new List<double>();
        var samples = new List<double>();
        for (int sample = 0; sample < 37; sample++)
        {
            if (sample % 2 == 0)
            {
                Measure(baseline, resetBaseline, oldSamples, sample);
                Measure(current, resetCurrent, samples, sample);
            }
            else
            {
                Measure(current, resetCurrent, samples, sample);
                Measure(baseline, resetBaseline, oldSamples, sample);
            }
        }
        return (Summarize(oldSamples), Summarize(samples));

        static void Measure(Action action, Action? reset, List<double> measurements, int sample)
        {
            reset?.Invoke();
            var timer = Stopwatch.StartNew();
            action();
            timer.Stop();
            if (sample >= 5)
            {
                measurements.Add(timer.Elapsed.TotalMilliseconds);
            }
        }
        static Timing Summarize(List<double> measurements)
        {
            measurements.Sort();
            return new(measurements[measurements.Count / 2], measurements[(int)Math.Ceiling(measurements.Count * .95) - 1]);
        }
    }

    private static void Write(string output, string name, object value)
    {
        File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
