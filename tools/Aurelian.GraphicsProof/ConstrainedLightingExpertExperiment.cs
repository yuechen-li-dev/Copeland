using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Shaders;
using static LightingExpertExperiment;

/// <summary>Authoring constraints specialize the existing decoder; no new runtime representation.</summary>
internal static class ConstrainedLightingExpertExperiment
{
    private const int TestSize = 64;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record Result(string Mode, Accuracy Accuracy, Accuracy BoundaryAccuracy,
        Accuracy CpuGpuAgreement, double GpuMilliseconds, double MaximumEdgeMismatch,
        object OneMillimetreProbe, string WeightsKey, JsonElement Compilation);

    public static void Run(string output, LightingCompilationExperiment.Room room, AurelianVulkanPlant plant,
        SceneLightingExpert global, string globalWeights, float[] reference, float[] repeat, bool[] visible)
    {
        string contractPath = Path.GetFullPath("tools/Aurelian.GraphicsProof/Assets/LightingFitContract.json");
        string contractKey = Hash(File.ReadAllBytes(contractPath));
        string compilerKey = Hash(File.ReadAllBytes("src/Aurelian/Aurelian.Assets/Lighting/Authoring/constrained_lighting_experts.py")
            .Concat(File.ReadAllBytes("src/Aurelian/Aurelian.Assets/Lighting/Authoring/local_lighting_experts.py")).ToArray());
        string trainingKey = Hash(File.ReadAllBytes(Path.Combine(output, "training.bin")));
        string decoderKey = Hash(File.ReadAllBytes("tools/Aurelian.GraphicsProof/Assets/LocalExpertDecoder.v.ts"));
        SceneLocalLightingExpert original = SceneLocalLightingExpert.Load(
            File.ReadAllBytes(Path.Combine(output, "loaded-local-expert.json")), global.SceneKey, decoderKey);
        bool[] boundary = LocalLightingExpertExperiment.BoundaryMask(room, visible);
        var results = new List<Result>();
        SceneLocalLightingExpert? exact = null;
        CompiledGraphicsProgram? exactRoomShader = null;
        foreach (string mode in new[] { "independent", "soft", "exact" })
        {
            byte[] manifest = File.ReadAllBytes(Path.Combine(output, "loaded-constrained-" + mode + ".json"));
            JsonElement compilation = ValidateCompilation(manifest, contractKey, compilerKey, trainingKey,
                original.WeightsKey, mode);
            SceneLocalLightingExpert expert = SceneLocalLightingExpert.Load(manifest, global.SceneKey, decoderKey);
            foreach (string name in new[] { "planes", "transforms", "uniformTransforms", "uniformWeights", "grid" })
            {
                Require(expert.Coefficients(name).SequenceEqual(original.Coefficients(name)),
                    "Constraint fit changed routing, coordinates or control models: " + name);
            }
            Require(expert.Children().SequenceEqual(original.Children()), "Constraint fit changed tree topology.");
            if (mode == "independent")
            {
                Accuracy refit = Compare(LocalLightingExpertExperiment.Predict(original, LightingExpertRepresentation.GeometryLocal),
                    LocalLightingExpertExperiment.Predict(expert, LightingExpertRepresentation.GeometryLocal), visible);
                Require(refit.MaximumError < .00001, "Joint independent fit did not reproduce the original local model: " + refit);
                JsonNode changedManifest = JsonNode.Parse(manifest)!;
                changedManifest["constraintCompilation"]!["ContractKey"] = "stale-contract";
                RequireRejected(() => ValidateCompilation(JsonSerializer.SerializeToUtf8Bytes(changedManifest),
                    contractKey, compilerKey, trainingKey, original.WeightsKey, mode));
                RequireRejected(() => ValidateCompilation(manifest, contractKey, "changed-compiler",
                    trainingKey, original.WeightsKey, mode));
                RequireRejected(() => ValidateCompilation(manifest, contractKey, compilerKey,
                    "changed-training", original.WeightsKey, mode));
                RequireRejected(() => ValidateCompilation(manifest, contractKey, compilerKey,
                    trainingKey, "changed-partition", mode));
            }
            string directory = Path.Combine(output, "constrained-" + mode + "-decoder");
            Directory.CreateDirectory(directory);
            string weights = LocalLightingExpertExperiment.GenerateWeights(expert);
            CompiledGraphicsProgram shader = Compile("ExpertProbe.v.ts", globalWeights, directory, localWeights: weights);
            float[] expected = LocalLightingExpertExperiment.Predict(expert, LightingExpertRepresentation.GeometryLocal);
            var actual = new float[reference.Length];
            for (int basis = 0; basis < 2; basis++)
            {
                using var batch = Infer(plant, shader, global.SceneKey, expert.WeightsKey,
                    LightingExpertRepresentation.GeometryLocal, basis, TestSize);
                float[] pixels = batch.ReadLinear();
                Require(batch.IsQualified, "Constrained decoder produced unresolved values.");
                for (int pixel = 0; pixel < visible.Length; pixel++)
                {
                    Array.Copy(pixels, pixel * 4, actual, pixel * 6 + basis * 3, 3);
                }
            }
            Accuracy agreement = Compare(expected, actual, Enumerable.Repeat(true, visible.Length).ToArray());
            Require(agreement.MaximumError < .001, "Constrained CPU/Vulkan disagreement: " + agreement);
            var times = new List<double>();
            for (int measurement = 0; measurement < 7; measurement++)
            {
                using var batch = Infer(plant, shader, global.SceneKey, expert.WeightsKey,
                    LightingExpertRepresentation.GeometryLocal, 2, 256);
                if (measurement >= 2)
                {
                    times.Add(batch.GpuMilliseconds());
                }
            }
            double mismatch = EdgeMismatch(expert, compilation);
            if (mode == "exact")
            {
                Require(mismatch < .000001, "Reloaded float coefficients violated declared C0: " + mismatch);
                exact = expert;
                exactRoomShader = Compile("ExpertSolid3D.v.ts", globalWeights, directory,
                    "export function ExpertChoice(): f32 {\n    return 4.0;\n}\n", weights);
            }
            results.Add(new(mode, Compare(reference, actual, visible), Compare(reference, actual, boundary),
                agreement, times.Order().ElementAt(2), mismatch, LocalLightingExpertExperiment.SeamProbe(expert, room),
                expert.WeightsKey, compilation));
            WritePreview(output, "constrained-" + mode, actual, visible);
            LocalLightingExpertExperiment.WriteError(output, "constrained-" + mode + "-error", reference, actual, visible);
        }
        Result independent = results.Single(result => result.Mode == "independent");
        Result constrained = results.Single(result => result.Mode == "exact");
        Require(constrained.MaximumEdgeMismatch < independent.MaximumEdgeMismatch * .001,
            "Exact constraints did not materially improve the motivating edge mismatch.");
        Require(exact is not null && exactRoomShader is not null, "Exact experiment failed to compile.");
        Vector3 sun = Vector3.Normalize(new Vector3(.6f, 1, .4f));
        byte[] first = RenderRoom(output, room, plant, exactRoomShader!, sun, new(7, 6, 9), "constrained-room-a", 1);
        byte[] second = RenderRoom(output, room, plant, exactRoomShader!, sun, new(-5, 4, 7), "constrained-room-b", 1);
        byte[] lampOff = RenderRoom(output, room, plant, exactRoomShader!, sun, new(7, 6, 9), "constrained-lamp-off", 0);
        int changed = ChangedPixels(first, lampOff);
        Require(changed > 100 && ChangedPixels(first, second) > 100, "Constrained camera/light variants failed.");
        Sheet(output, "constrained-comparison.png", ["cycles-reference", "constrained-independent", "constrained-soft",
            "constrained-exact", "constrained-independent-error", "constrained-soft-error", "constrained-exact-error", "constrained-room-a"]);
        File.Copy(contractPath, Path.Combine(output, "lighting-fit-contract.json"), true);
        File.WriteAllText(Path.Combine(output, "constrained-expert-evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            global.SceneKey,
            ContractKey = contractKey,
            CompilerKey = compilerKey,
            TrainingKey = trainingKey,
            PartitionKey = original.WeightsKey,
            NativeUsdRoundTrip = true,
            Results = results,
            SamplingNoise = Compare(reference, repeat, visible),
            BoundarySamplingNoise = Compare(reference, repeat, boundary),
            VisibleReceivers = visible.Count(value => value),
            BoundaryReceivers = boundary.Count(value => value),
            ParameterBytes = 2188,
            RuntimeGeometryQueries = 0,
            RuntimeConstraintSolving = false,
            RuntimeDecoderChanged = false,
            StaleContractCompilerTrainingAndPartitionRejected = true,
            ExactHasLowerOverallRmse = constrained.Accuracy.Rmse < independent.Accuracy.Rmse,
            ExactHasLowerBoundaryRmse = constrained.BoundaryAccuracy.Rmse < independent.BoundaryAccuracy.Rmse,
            LampOffChangedPixels = changed,
            Limits = new[]
            {
                "One authored continuous diffuse floor; source declaration must be physically valid",
                "C0 constrains values, not derivatives; real geometry/material discontinuities need explicit exemptions",
                "Exact edge equality is a CPU polynomial check on native-reloaded float coefficients; Vulkan agreement is tested at held-out receiver centres",
                "One-millimetre pairs contain legitimate gradients and have no independently baked one-millimetre reference",
                "Finite cached 8192-sample/eight-bounce reference; training solve is offline NumPy CPU work",
                "Continuity is not correctness: compare held-out quality separately; no test-derived tuning or new-scene generalization",
            },
        }, JsonOptions));
        Console.WriteLine("AURELIAN_CONSTRAINED_LIGHTING_EXPERTS_PASSED " + plant.Facts.PhysicalDeviceName);
    }

    private static JsonElement ValidateCompilation(byte[] manifest, string contractKey, string compilerKey,
        string trainingKey, string partitionKey, string mode)
    {
        using var document = JsonDocument.Parse(manifest);
        JsonElement compilation = document.RootElement.GetProperty("constraintCompilation");
        if (compilation.GetProperty("ContractKey").GetString() != contractKey
            || compilation.GetProperty("CompilerKey").GetString() != compilerKey
            || compilation.GetProperty("TrainingKey").GetString() != trainingKey
            || compilation.GetProperty("PartitionKey").GetString() != partitionKey
            || compilation.GetProperty("Mode").GetString() != mode
            || compilation.GetProperty("ReferenceInputs").GetArrayLength() != 1
            || compilation.GetProperty("ReferenceInputs")[0].GetString() != "training.bin")
        {
            throw new InvalidDataException("Lighting constraint compilation identity mismatch.");
        }
        return compilation.Clone();
    }

    private static double EdgeMismatch(SceneLocalLightingExpert expert, JsonElement compilation)
    {
        float[] transforms = expert.Coefficients("transforms");
        float[] weights = expert.Coefficients("weights");
        string[] exceptions = compilation.GetProperty("Contract").GetProperty("discontinuousSeams")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        double maximum = 0;
        foreach (JsonElement edge in compilation.GetProperty("SharedEdges").EnumerateArray())
        {
            if (exceptions.Contains(edge.GetProperty("name").GetString(), StringComparer.Ordinal))
            {
                continue;
            }
            int first = edge.GetProperty("first").GetInt32();
            int second = edge.GetProperty("second").GetInt32();
            double[] start = edge.GetProperty("start").EnumerateArray().Select(value => value.GetDouble()).ToArray();
            double[] end = edge.GetProperty("end").EnumerateArray().Select(value => value.GetDouble()).ToArray();
            // More samples than the four collocation rows; this checks float round-trip equality separately.
            for (int sample = 0; sample <= 128; sample++)
            {
                double x = start[0] + (end[0] - start[0]) * sample / 128;
                double z = start[1] + (end[1] - start[1]) * sample / 128;
                for (int channel = 0; channel < 6; channel++)
                {
                    maximum = Math.Max(maximum, Math.Abs(Value(first, x, z, channel) - Value(second, x, z, channel)));
                }
            }
        }
        return maximum;

        double Value(int leaf, double pointX, double pointZ, int channel)
        {
            int offset = leaf * 4;
            double x = (pointX - transforms[offset]) * transforms[offset + 2];
            double z = (pointZ - transforms[offset + 1]) * transforms[offset + 3];
            double[] features = [1, x, z, x * x, x * z, z * z, x * x * x, x * x * z, x * z * z, z * z * z];
            double result = 0;
            for (int feature = 0; feature < features.Length; feature++)
            {
                result += features[feature] * weights[(leaf * 10 + feature) * 6 + channel];
            }
            return result;
        }
    }

    private static void RequireRejected(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException("Stale constraint compilation was admitted.");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void Require(bool condition, string message) => LightingExpertExperiment.Require(condition, message);
}
