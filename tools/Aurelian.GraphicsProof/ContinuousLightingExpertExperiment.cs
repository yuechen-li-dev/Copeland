using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Shaders;
using static LightingExpertExperiment;

internal static class ContinuousLightingExpertExperiment
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record Result(string Profile, Accuracy Accuracy, Accuracy BoundaryAccuracy, Accuracy CpuGpuAgreement,
        Accuracy DenseCpuGpuAgreement,
        double GpuMilliseconds, double CpuEdgeMismatch, double GpuEdgeMismatch, int CoefficientCount,
        int NumericPayloadBytes, int ShaderBytes, JsonElement Compilation);

    public static void Run(string output, LightingCompilationExperiment.Room room, AurelianVulkanPlant plant,
        SceneLightingExpert global, string globalWeights, float[] reference, float[] repeat, bool[] visible, bool adaptive = false)
    {
        string prefix = adaptive ? "adaptive" : "continuous";
        string contractName = adaptive ? "AdaptiveFitContract.json" : "ContinuousFitContract.json";
        string contractPath = Path.GetFullPath(Path.Combine("tools/Aurelian.GraphicsProof/Assets", contractName));
        string contractKey = Hash(File.ReadAllBytes(contractPath));
        using var contract = JsonDocument.Parse(File.ReadAllBytes(contractPath));
        string[] profiles = contract.RootElement.GetProperty("profiles").EnumerateArray()
            .Select(profile => profile.GetProperty("name").GetString()!).ToArray();
        string compilerKey = Hash(File.ReadAllBytes("src/Aurelian/Aurelian.Assets/Lighting/Authoring/continuous_lighting_experts.py")
            .Concat(File.ReadAllBytes("src/Aurelian/Aurelian.Assets/Lighting/Authoring/constrained_lighting_experts.py"))
            .Concat(File.ReadAllBytes("src/Aurelian/Aurelian.Assets/Lighting/Authoring/local_lighting_experts.py")).ToArray());
        if (adaptive)
        {
            compilerKey = Hash(File.ReadAllBytes("src/Aurelian/Aurelian.Assets/Lighting/Authoring/adaptive_lighting_experts.py")
                .Concat(Convert.FromHexString(compilerKey)).ToArray());
        }
        string trainingKey = Hash(File.ReadAllBytes(Path.Combine(output, "training.bin")));
        string localDecoderKey = Hash(File.ReadAllBytes("tools/Aurelian.GraphicsProof/Assets/LocalExpertDecoder.v.ts"));
        string decoderKey = Hash(File.ReadAllBytes("tools/Aurelian.GraphicsProof/Assets/ContinuousExpertDecoder.v.ts"));
        var partition = SceneLocalLightingExpert.Load(File.ReadAllBytes(Path.Combine(output, "loaded-local-expert.json")),
            global.SceneKey, localDecoderKey);
        bool[] boundary = LocalLightingExpertExperiment.BoundaryMask(room, visible);
        var results = new List<Result>();
        var models = new Dictionary<string, ContinuousLightingModel>();
        foreach (string profile in profiles)
        {
            byte[] manifest = File.ReadAllBytes(Path.Combine(output, "loaded-" + prefix + "-" + profile + ".json"));
            ValidateCompilation(manifest, contractKey, compilerKey, trainingKey, partition.WeightsKey, profile);
            ContinuousLightingModel model = ContinuousLightingModel.Load(manifest, partition, decoderKey);
            if (adaptive)
            {
                int budget = contract.RootElement.GetProperty("numericBudgetBytes").GetInt32();
                Require(model.PayloadBytes <= budget, "Adaptive numeric payload exceeded the authored budget.");
                Require(model.Compilation.GetProperty("NumericPayloadBytes").GetInt32() == model.PayloadBytes,
                    "Adaptive payload accounting disagrees with the loaded artifact.");
                if (model.Compilation.GetProperty("RefinementPolicy").GetString() != "none")
                {
                    Require(model.PayloadBytes == budget, "Matched-budget refinement did not fill the declared budget.");
                    Require(model.Compilation.GetProperty("RefinementHistory").GetArrayLength()
                        == contract.RootElement.GetProperty("interiorEdgeSplits").GetInt32(), "Adaptive refinement count mismatch.");
                }
            }
            Require(model.SharedEdgeCount <= 128, "Seam probe height cannot visit every retained edge.");
            models.Add(profile, model);
            if (profile == profiles[0])
            {
                Reject(() => ValidateCompilation(manifest, "stale-contract", compilerKey, trainingKey, partition.WeightsKey, profile));
                Reject(() => ValidateCompilation(manifest, contractKey, "stale-compiler", trainingKey, partition.WeightsKey, profile));
                Reject(() => ValidateCompilation(manifest, contractKey, compilerKey, "stale-training", partition.WeightsKey, profile));
                Reject(() => ValidateCompilation(manifest, contractKey, compilerKey, trainingKey, "stale-partition", profile));
                Reject(() => ContinuousLightingModel.Load(manifest, partition, "stale-decoder"));
                JsonNode altered = JsonNode.Parse(manifest)!;
                altered["weightsSha256"] = new string('0', 64);
                Reject(() => ContinuousLightingModel.Load(JsonSerializer.SerializeToUtf8Bytes(altered), partition, decoderKey));
                // Dense CPU coverage is separate from the sparse reference evaluation.
                for (int row = 0; row < 256; row++)
                {
                    for (int column = 0; column < 256; column++)
                    {
                        model.Evaluate(new(-2.7f + (column + .5f) * 5.4f / 256, -2.7f + (row + .5f) * 5.4f / 256));
                    }
                }
            }
            string directory = Path.Combine(output, prefix + "-" + profile + "-decoder");
            Directory.CreateDirectory(directory);
            string weights = model.GenerateWeights();
            CompiledGraphicsProgram shader = Compile("ExpertProbe.v.ts", globalWeights, directory, continuousWeights: weights);
            float[] expected = Predict(model);
            var actual = new float[reference.Length];
            for (int basis = 0; basis < 2; basis++)
            {
                using var batch = Infer(plant, shader, global.SceneKey, model.WeightsKey,
                    LightingExpertRepresentation.GeometryLocal, basis, 64);
                float[] pixels = batch.ReadLinear();
                Require(batch.IsQualified, "Continuous inference unresolved.");
                for (int pixel = 0; pixel < visible.Length; pixel++)
                {
                    Array.Copy(pixels, pixel * 4, actual, pixel * 6 + basis * 3, 3);
                }
            }
            Accuracy agreement = Compare(expected, actual, Enumerable.Repeat(true, visible.Length).ToArray());
            Require(agreement.MaximumError < .001, "Continuous CPU/Vulkan disagreement: " + agreement);
            var times = new List<double>();
            Accuracy? denseAgreement = null;
            for (int measurement = 0; measurement < 7; measurement++)
            {
                using var batch = Infer(plant, shader, global.SceneKey, model.WeightsKey,
                    LightingExpertRepresentation.GeometryLocal, 2, 256);
                if (measurement == 0)
                {
                    float[] pixels = batch.ReadLinear();
                    var denseActual = new float[256 * 256 * 3];
                    var denseExpected = new float[denseActual.Length];
                    for (int pixel = 0; pixel < 256 * 256; pixel++)
                    {
                        Vector2 point = new(-2.7f + (pixel % 256 + .5f) * 5.4f / 256,
                            -2.7f + (pixel / 256 + .5f) * 5.4f / 256);
                        Vector3 value = model.Evaluate(point);
                        denseExpected[pixel * 3] = value.X;
                        denseExpected[pixel * 3 + 1] = value.Y;
                        denseExpected[pixel * 3 + 2] = value.Z;
                        Array.Copy(pixels, pixel * 4, denseActual, pixel * 3, 3);
                    }
                    denseAgreement = Compare(denseExpected, denseActual,
                        Enumerable.Repeat(true, 256 * 256).ToArray(), channelsPerReceiver: 3);
                    Require(denseAgreement.MaximumError < .001, "Dense continuous GPU coverage/agreement failed: " + denseAgreement);
                }
                if (measurement >= 2)
                {
                    times.Add(batch.GpuMilliseconds());
                }
            }
            string seamDirectory = Path.Combine(directory, "seam-probe");
            Directory.CreateDirectory(seamDirectory);
            CompiledGraphicsProgram seamShader = Compile("ExpertProbe.v.ts", globalWeights, seamDirectory,
                continuousWeights: weights, sceneDecoder: "ContinuousSeamDecoder");
            using var seamBatch = Infer(plant, seamShader, global.SceneKey, model.WeightsKey,
                LightingExpertRepresentation.GeometryLocal, 2, 128);
            float[] differences = seamBatch.ReadLinear();
            Require(seamBatch.IsQualified, "Continuous GPU seam probe unresolved.");
            double gpuEdgeMismatch = differences.Where((_, index) => index % 4 != 3).Max();
            double cpuEdgeMismatch = model.MaximumEdgeMismatch();
            Require(cpuEdgeMismatch < .000001 && gpuEdgeMismatch < .00002,
                "Shared boundary continuity failed: CPU=" + cpuEdgeMismatch + "; GPU=" + gpuEdgeMismatch);
            int shaderBytes = shader.Shaders.Stages.Sum(stage => stage.SpirvBytes.Length);
            foreach (var stage in shader.Shaders.Stages)
            {
                File.WriteAllBytes(Path.Combine(directory, "ExpertProbe." + stage.Stage + ".spv"), stage.SpirvBytes);
            }
            results.Add(new(profile, Compare(reference, actual, visible), Compare(reference, actual, boundary), agreement, denseAgreement!,
                times.Order().ElementAt(2), cpuEdgeMismatch, gpuEdgeMismatch, model.CoefficientCount,
                model.PayloadBytes, shaderBytes, model.Compilation));
            WritePreview(output, prefix + "-" + profile, actual, visible);
            LocalLightingExpertExperiment.WriteError(output, prefix + "-" + profile + "-error", reference, actual, visible);
        }
        Result selected = results.OrderBy(result => result.Accuracy.Rmse).First();
        ContinuousLightingModel best = models[selected.Profile];
        string roomDirectory = Path.Combine(output, prefix + "-room-decoder");
        Directory.CreateDirectory(roomDirectory);
        CompiledGraphicsProgram roomShader = Compile("ExpertSolid3D.v.ts", globalWeights, roomDirectory,
            "export function ExpertChoice(): f32 {\n    return 4.0;\n}\n", continuousWeights: best.GenerateWeights());
        Vector3 sun = Vector3.Normalize(new Vector3(.6f, 1, .4f));
        byte[] first = RenderRoom(output, room, plant, roomShader, sun, new(7, 6, 9), prefix + "-room-a", 1);
        byte[] second = RenderRoom(output, room, plant, roomShader, sun, new(-5, 4, 7), prefix + "-room-b", 1);
        byte[] off = RenderRoom(output, room, plant, roomShader, sun, new(7, 6, 9), prefix + "-lamp-off", 0);
        Require(ChangedPixels(first, off) > 100 && ChangedPixels(first, second) > 100, "Continuous camera/light variants failed.");
        Vector2 query = new(-1, 1);
        Require(Vector3.Distance(best.Evaluate(query, 2, .3f), best.Evaluate(query, 1, 0) * 2 + best.Evaluate(query, 0, 1) * .3f) < .000001,
            "Continuous basis scaling is not linear.");
        Sheet(output, prefix + "-comparison.png", ["cycles-reference", "geometrylocal", "constrained-exact",
            prefix + "-" + selected.Profile, "geometrylocal-error", "constrained-exact-error",
            prefix + "-" + selected.Profile + "-error", prefix + "-room-a"]);
        Require(profiles.Length == 4, "Control sheet requires four declared profiles.");
        string[] controlImages = profiles.Select(profile => prefix + "-" + profile)
            .Concat(profiles.Select(profile => prefix + "-" + profile + "-error")).ToArray();
        Sheet(output, prefix + "-controls.png", controlImages);
        using var previous = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "constrained-expert-evidence.json")));
        JsonElement previousResults = previous.RootElement.GetProperty("Results").Clone();
        double oldExact = previousResults.EnumerateArray().Single(result => result.GetProperty("Mode").GetString() == "exact")
            .GetProperty("Accuracy").GetProperty("Rmse").GetDouble();
        Result? residual = results.SingleOrDefault(result => result.Profile == "residual");
        Result[] matchedControls = results.Where(result => result.Profile is "longest" or "random").ToArray();
        File.WriteAllText(Path.Combine(output, prefix + "-expert-evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true,
            AcceptanceScope = "Native experiment execution and CPU/GPU continuity; separate from default-model adoption",
            Device = plant.Facts.PhysicalDeviceName,
            global.SceneKey,
            ContractKey = contractKey,
            CompilerKey = compilerKey,
            TrainingKey = trainingKey,
            PartitionKey = partition.WeightsKey,
            NativeUsdRoundTrip = true,
            Results = results,
            TrainingSelectedProfile = adaptive ? "residual" : null,
            AdaptiveBeatsEveryMatchedControl = adaptive && matchedControls.All(result => residual!.Accuracy.Rmse < result.Accuracy.Rmse),
            AdaptiveBoundaryBeatsEveryMatchedControl = adaptive && matchedControls.All(result => residual!.BoundaryAccuracy.Rmse < result.BoundaryAccuracy.Rmse),
            PreviousCubicFits = previousResults,
            BestByHeldOutRmse = selected.Profile,
            BestImprovesPreviousContinuousCubic = selected.Accuracy.Rmse < oldExact,
            PreviousOverallRmseGate = .004,
            MeetsPreviousOverallRmseGate = selected.Accuracy.Rmse <= .004,
            Vertices = best.VertexCount,
            Triangles = best.TriangleCount,
            SharedEdges = best.SharedEdgeCount,
            GpuSeamProbeSize = 128,
            SamplingNoise = Compare(reference, repeat, visible),
            BoundarySamplingNoise = Compare(reference, repeat, boundary),
            LampOffChangedPixels = ChangedPixels(first, off),
            RuntimeConstraintSolving = false,
            RuntimeGeometryQueries = 0,
            StaleCompilationAndPayloadRejected = true,
            Limits = new[]
            {
                "Same static diffuse floor and finite cached Cycles reference; no new-scene qualification",
                "Profiles fixed before measurement; best label is exploratory held-out selection, not a blind certification",
                adaptive ? "Three refined profiles have identical numeric payload budgets; baseline is smaller; compiled shader bytes can differ"
                    : "Budgets differ; effective/observed freedom, numeric payload and compiled shader bytes are reported separately",
                "GPU seam test forces both adjacent triangles at identical edge positions; covers shared boundaries, not derivatives or physical lighting accuracy",
                "Finite fan triangulation derived from this eight-leaf tree; one-micro-unit normalized containment tolerance for floating-point rounding",
                "Offline NumPy solve runs on CPU; Vulkan handles runtime decoding; kernel timing excludes submission/readback/presentation",
            },
        }, JsonOptions));
        File.Copy(contractPath, Path.Combine(output, prefix + "-fit-contract.json"), true);
        Console.WriteLine("AURELIAN_" + prefix.ToUpperInvariant() + "_LIGHTING_EXPERTS_PASSED " + plant.Facts.PhysicalDeviceName);
    }

    private static float[] Predict(ContinuousLightingModel model)
    {
        var result = new float[64 * 64 * 6];
        for (int pixel = 0; pixel < 64 * 64; pixel++)
        {
            Vector3 sun = model.Evaluate(Position(pixel), 1, 0);
            Vector3 lamp = model.Evaluate(Position(pixel), 0, 1);
            float[] values = [sun.X, sun.Y, sun.Z, lamp.X, lamp.Y, lamp.Z];
            Array.Copy(values, 0, result, pixel * 6, 6);
        }
        return result;
    }

    private static void ValidateCompilation(byte[] manifest, string contractKey, string compilerKey,
        string trainingKey, string partitionKey, string profile)
    {
        using var document = JsonDocument.Parse(manifest);
        JsonElement compilation = document.RootElement.GetProperty("compilation");
        if (compilation.GetProperty("ContractKey").GetString() != contractKey
            || compilation.GetProperty("CompilerKey").GetString() != compilerKey
            || compilation.GetProperty("TrainingKey").GetString() != trainingKey
            || compilation.GetProperty("PartitionKey").GetString() != partitionKey
            || compilation.GetProperty("Profile").GetString() != profile
            || compilation.GetProperty("ReferenceInputs").GetArrayLength() != 1
            || compilation.GetProperty("ReferenceInputs")[0].GetString() != "training.bin")
        {
            throw new InvalidDataException("Continuous compilation identity mismatch.");
        }
    }

    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException("Stale continuous artifact was admitted.");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Require(bool condition, string message) => LightingExpertExperiment.Require(condition, message);
}
