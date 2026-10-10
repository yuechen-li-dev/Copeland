using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.GameHost.Silk;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Lighting.Aetheris;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Rendering.Contracts.Shaders;
using Aurelian.Runtime;
using Aurelian.Runtime.Dominatus.Inspection;
using Aurelian.Shaders.Graphics;
using Copeland.TS.Gpu;
using Copeland.TS.Gpu.VdMir;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using SkiaSharp;

internal static class LightingExpertExperiment
{
    private const int TestSize = 64;
    private const int TimingSize = 256;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal sealed record Accuracy(double Rmse, double MaximumError, double RelativeRmse);

    public static void Run(string output, string? blender, bool reuseReference, bool localExperts = false,
        bool constrainedExperts = false, bool continuousExperts = false, bool adaptiveExperts = false)
    {
        Directory.CreateDirectory(output);
        string evidencePath = Path.Combine(output, "expert-evidence.json");
        File.WriteAllText(evidencePath, "{\"accepted\":false}");
        if (localExperts)
        {
            File.WriteAllText(Path.Combine(output, "local-expert-evidence.json"), "{\"accepted\":false}");
        }
        if (constrainedExperts)
        {
            File.WriteAllText(Path.Combine(output, "constrained-expert-evidence.json"), "{\"accepted\":false}");
        }
        if (continuousExperts)
        {
            File.WriteAllText(Path.Combine(output, "continuous-expert-evidence.json"), "{\"accepted\":false}");
        }
        if (adaptiveExperts)
        {
            File.WriteAllText(Path.Combine(output, "adaptive-expert-evidence.json"), "{\"accepted\":false}");
        }
        var room = LightingCompilationExperiment.BuildRoom();
        Vector3 sun = Vector3.Normalize(new Vector3(.6f, 1, .4f));
        string sceneKey = Hash(Encoding.UTF8.GetBytes(room.Lighting.Field.Program.StructuralHash + "|"
            + room.Lighting.MaterialIdentity + "|unit-sun:.6,1,.4|floor:5.4m|cycles-sun-indirect-lamp-total/2"));
        string decoderPath = Path.GetFullPath("tools/Aurelian.GraphicsProof/Assets/ExpertDecoder.v.ts");
        string fixturePath = Path.Combine(output, "fixture.json");
        File.WriteAllText(fixturePath, JsonSerializer.Serialize(new
        {
            SceneKey = sceneKey,
            Sun = new[] { sun.X, sun.Y, sun.Z },
            Bodies = room.Bodies.Select(body => new
            {
                body.Declaration.Id,
                Positions = LightingCompilationExperiment.Geometry(body).Select(vertex =>
                    new[] { vertex.Position.X, vertex.Position.Y, vertex.Position.Z }),
                Albedo = new[] { body.Declaration.Albedo.X, body.Declaration.Albedo.Y, body.Declaration.Albedo.Z },
                Emission = new[] { body.Declaration.Emission.X, body.Declaration.Emission.Y, body.Declaration.Emission.Z },
            }),
        }, JsonOptions));
        RunBlender(blender, fixturePath, output, decoderPath, reuseReference, localExperts, constrainedExperts, continuousExperts, adaptiveExperts);
        byte[] artifact = File.ReadAllBytes(Path.Combine(output, "loaded-expert.json"));
        string decoderKey = Hash(File.ReadAllBytes(decoderPath));
        SceneLightingExpert expert = SceneLightingExpert.Load(artifact, sceneKey, decoderKey);
        RequireRejects(() => SceneLightingExpert.Load(artifact, "changed-scene", decoderKey), "Changed scene accepted stale weights.");
        RequireRejects(() => SceneLightingExpert.Load(artifact, sceneKey, "changed-decoder"), "Changed decoder accepted stale weights.");
        string altered = Encoding.UTF8.GetString(artifact).Replace(expert.WeightsKey, new string('0', 64), StringComparison.Ordinal);
        RequireRejects(() => SceneLightingExpert.Load(Encoding.UTF8.GetBytes(altered), sceneKey, decoderKey), "Bad checksum accepted.");

        float[] reference = Read(Path.Combine(output, "testing.bin"), TestSize * TestSize * 6);
        float[] repeat = Read(Path.Combine(output, "repeat.bin"), reference.Length);
        bool[] mask = ReceiverMask(room);
        Accuracy samplingNoise = Compare(reference, repeat, mask);
        Require(reference.Where((_, index) => index % 6 >= 3).Max() > .1f, "Cycles lamp basis has no illumination.");
        Require(reference.Where((_, index) => index % 6 < 3).Max() > .02f, "Cycles sun basis has no indirect illumination.");
        WritePreview(output, "cycles-reference", reference, mask);
        var results = new List<object>();
        var qualifications = new List<LightingExpertQualification>();
        var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero,
            new VulkanPlantOptions(EnableValidation: true, ApplicationName: "Aurelian Scene Lighting Experts"));
        Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
        using var plant = initialized.Plant!;
        string weightsSource = GenerateWeights(expert);
        File.WriteAllText(Path.Combine(output, "ExpertWeights.v.ts"), weightsSource);
        CompiledGraphicsProgram probe = Compile("ExpertProbe.v.ts", weightsSource, output);
        foreach (LightingExpertRepresentation representation in new[]
        {
            LightingExpertRepresentation.Polynomial, LightingExpertRepresentation.CoarseGrid,
            LightingExpertRepresentation.Neural, LightingExpertRepresentation.Hybrid,
        })
        {
            float[] expected = Predict(expert, representation);
            float[] actual = new float[reference.Length];
            for (int basis = 0; basis < 2; basis++)
            {
                using var batch = Inference(representation, basis, TestSize);
                float[] values = batch.ReadLinear();
                Require(batch.IsQualified, "Expert inference produced invalid output.");
                for (int pixel = 0; pixel < TestSize * TestSize; pixel++)
                {
                    Array.Copy(values, pixel * 4, actual, pixel * 6 + basis * 3, 3);
                }
            }
            Accuracy agreement = Compare(expected, actual, Enumerable.Repeat(true, mask.Length).ToArray());
            Require(agreement.MaximumError < .001, "CPU/Vulkan expert disagreement: " + agreement);
            Accuracy accuracy = Compare(reference, actual, mask);
            var gpuTimes = new List<double>();
            for (int measurement = 0; measurement < 7; measurement++)
            {
                using var batch = Inference(representation, 2, TimingSize);
                if (measurement >= 2)
                {
                    gpuTimes.Add(batch.GpuMilliseconds());
                }
            }
            double median = gpuTimes.Order().ElementAt(2);
            qualifications.Add(new(sceneKey, representation, accuracy.Rmse, median));
            results.Add(new
            {
                Representation = representation.ToString(),
                Accuracy = accuracy,
                CpuGpuAgreement = agreement,
                GpuMedianMilliseconds = median,
                TimedPixels = TimingSize * TimingSize,
                CoefficientBytes = PayloadBytes(representation),
            });
            WritePreview(output, representation.ToString().ToLowerInvariant(), actual, mask);
        }
        // Explicit measured quality gate, then cost. No stale expert can win on utility.
        double threshold = .01;
        var misleading = new LightingExpertQualification("changed-scene", LightingExpertRepresentation.Polynomial, 0, .000001);
        qualifications.Add(misleading);
        LightingExpertQualification selected = SceneLightingExpert.Select(qualifications, sceneKey, threshold);
        selected = SelectWithDominatus(qualifications, sceneKey, threshold, selected, output);
        Require(selected.SceneKey == sceneKey, "Utility bypassed scene validity.");
        RequireNoCandidate(() => SceneLightingExpert.Select([misleading], sceneKey, threshold));
        object[] qualitySweep = new[] { .04, .01, .006, .001 }.Select(limit =>
        {
            var admitted = SceneLightingExpert.Admit(qualifications, sceneKey, limit);
            return (object)new
            {
                MaximumRmse = limit,
                Selected = admitted.FirstOrDefault()?.Representation.ToString(),
                Admitted = admitted.Select(item => item.Representation.ToString()).ToArray(),
            };
        }).ToArray();
        Vector3 query = expert.Evaluate(new(-1, 1), selected.Representation);
        Vector3 combination = expert.Evaluate(new(-1, 1), selected.Representation, 2, .3f);
        Vector3 explicitSum = expert.Evaluate(new(-1, 1), selected.Representation, 1, 0) * 2
            + expert.Evaluate(new(-1, 1), selected.Representation, 0, 1) * .3f;
        Require(Vector3.Distance(combination, explicitSum) < .000001f && query.Length() > 0, "Lighting bases are not linear.");
        byte[] roomView = CaptureRoom(selected.Representation, new(7, 6, 9), "room-view-a", 1);
        byte[] otherView = CaptureRoom(selected.Representation, new(-5, 4, 7), "room-view-b", 1);
        byte[] lampOff = CaptureRoom(selected.Representation, new(7, 6, 9), "room-lamp-off", 0);
        int lampChangedPixels = ChangedPixels(roomView, lampOff);
        Require(lampChangedPixels > 100, "Turning off the compiled lamp did not change the rendered receiver.");
        Require(ChangedPixels(roomView, otherView) > 100, "Changing the camera did not change the rendered room.");
        Sheet(output);
        Directory.CreateDirectory(Path.Combine(output, "licenses"));
        foreach (string name in new[] { "GPL-3.0", "AGPL-3.0" })
        {
            File.WriteAllText(Path.Combine(output, "licenses", name + ".txt"), AetherisLightField.LicenseText(name));
        }
        File.Copy("src/Integrations/Aurelian.Lighting.Aetheris/NOTICE.md", Path.Combine(output, "NOTICE.md"), true);
        File.WriteAllText(evidencePath, JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            SceneKey = sceneKey,
            expert.WeightsKey,
            NativeUsdRoundTrip = true,
            ArtifactBytes = new FileInfo(Path.Combine(output, "lighting.usda")).Length,
            ReferenceBytes = new FileInfo(Path.Combine(output, "training.bin")).Length,
            TrainingReceivers = 48 * 48,
            HeldOutVisibleReceivers = mask.Count(value => value),
            SamplingNoise = samplingNoise,
            QualityThreshold = threshold,
            QualitySweep = qualitySweep,
            Selected = selected.Representation.ToString(),
            Results = results,
            StaleSceneRejected = true,
            DecoderMismatchRejected = true,
            ChecksumMismatchRejected = true,
            LinearCoefficientsVerified = true,
            DominatusUtilitySelection = true,
            LampOffChangedPixels = lampChangedPixels,
            RuntimeGeometryQueries = 0,
            RuntimeTraining = false,
            Limits = new[]
            {
                "Visible floor diffuse lighting only; sun indirect and lamp direct-plus-indirect; static geometry/materials, two fixed source bases",
                "Cycles GPU Monte Carlo reference uses 8192 samples and an eight-bounce cap, not an exact or unbiased infinite-bounce solution",
                "Fixed ReLU hidden features and ridge-trained output weights; offline NumPy fitting runs on CPU",
                "Pixel probes include RGBA16F quantization; mesh shader evaluates the decoder directly without an intermediate lighting map",
                "All models and the scene are carried by USD; per-representation coefficient bytes exclude scene and decoder overhead",
                "GPU times exclude pipeline compilation, submission, readback and presentation; no tensor-core or multi-GPU qualification",
            },
        }, JsonOptions));
        Console.WriteLine("AURELIAN_LIGHTING_EXPERTS_PASSED " + plant.Facts.PhysicalDeviceName);
        if (localExperts)
        {
            LocalLightingExpertExperiment.Run(output, room, plant, expert, weightsSource, reference, repeat, mask);
        }
        if (constrainedExperts)
        {
            ConstrainedLightingExpertExperiment.Run(output, room, plant, expert, weightsSource, reference, repeat, mask);
        }
        if (continuousExperts)
        {
            ContinuousLightingExpertExperiment.Run(output, room, plant, expert, weightsSource, reference, repeat, mask);
        }
        if (adaptiveExperts)
        {
            ContinuousLightingExpertExperiment.Run(output, room, plant, expert, weightsSource, reference, repeat, mask, adaptive: true);
        }

        VulkanLightingBake Inference(LightingExpertRepresentation representation, int basis, int size)
        {
            return Infer(plant, probe, sceneKey, expert.WeightsKey, representation, basis, size);
        }

        byte[] CaptureRoom(LightingExpertRepresentation representation, Vector3 eye, string name, float lamp)
        {
            string choice = "export function ExpertChoice(): f32 {\n    return "
                + ((int)representation).ToString(CultureInfo.InvariantCulture) + ".0;\n}\n";
            CompiledGraphicsProgram shader = Compile("ExpertSolid3D.v.ts", weightsSource, output, choice);
            return RenderRoom(output, room, plant, shader, sun, eye, name, lamp);
        }
    }

    internal static VulkanLightingBake Infer(AurelianVulkanPlant plant, CompiledGraphicsProgram shader,
        string sceneKey, string weightsKey, LightingExpertRepresentation representation, int basis, int size)
    {
        float[] parameters = new float[24];
        parameters[12] = (int)representation;
        parameters[16] = basis == 1 ? 0 : 1;
        parameters[17] = basis == 0 ? 0 : 1;
        var plan = new LightingCompilation("expert." + representation, LightingArtifactKind.DiffuseTransfer,
            new(sceneKey, weightsKey, "fixed-sun-and-lamp", VulkanLightingBake.ParameterIdentity(parameters),
                LightingProgramIdentity.Compute(shader)), size, size, 1, 1, linearLightResponse: true);
        var batch = new VulkanLightingBake(plant, plan, shader, parameters);
        try
        {
            batch.Submit(1);
            var timeout = Stopwatch.StartNew();
            while (!batch.IsComplete())
            {
                Require(timeout.Elapsed.TotalSeconds < 20, "Inference fence timed out.");
                Thread.Sleep(1);
            }
            return batch;
        }
        catch
        {
            batch.Dispose();
            throw;
        }
    }

    internal static byte[] RenderRoom(string output, LightingCompilationExperiment.Room room,
        AurelianVulkanPlant plant, CompiledGraphicsProgram shader, Vector3 sun, Vector3 eye, string name, float lamp)
    {
        var assets = new GameAssets();
        using var target = new VulkanNativeFrameTarget(plant, 512, 512);
        using var renderer = new VulkanSolid3DRenderer(plant, shader, target,
            shadowProgram: assets.Shader("Shadow3D.v.ts"), outputProgram: assets.Shader("ToneMap3D.v.ts"));
        renderer.Settings = Graphics3DSettings.Default with
        {
            SunDirection = sun,
            SunIntensity = 1,
            SkyAmbient = new(lamp),
            GroundAmbient = Vector3.Zero,
        };
        Matrix4x4 camera = Camera3D.Matrix(eye, Vector3.Normalize(new Vector3(0, 1, 0) - eye), 1);
        var first = renderer.Render(room.Display, camera, eye, new(.02f, .025f, .03f, 1), capture: true);
        var repeatFrame = renderer.Render(room.Display, camera, eye, new(.02f, .025f, .03f, 1), capture: true);
        Require(first.PixelSha256 == repeatFrame.PixelSha256, "Repeated mesh inference changed pixels.");
        NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), 512, 512, first.Pixels!);
        return first.Pixels!;
    }

    internal static LightingExpertQualification SelectWithDominatus(
        IReadOnlyList<LightingExpertQualification> candidates, string sceneKey, double threshold,
        LightingExpertQualification expected, string output, string inspectionName = "expert-inspection.json")
    {
        IReadOnlyList<LightingExpertQualification> eligible = SceneLightingExpert.Admit(candidates, sceneKey, threshold);
        var runtime = new AurelianAgentRuntime(256);
        var root = StateId.Of("lighting.expert.Choose");
        var graph = new HfsmGraph { Root = root };
        var selectedKey = new BbKey<string>("lighting.expert.selected");
        var sceneKeyFact = new BbKey<string>("lighting.expert.scene");
        var errorKey = new BbKey<double>("lighting.expert.rmse");
        var costKey = new BbKey<double>("lighting.expert.gpuMilliseconds");
        var options = new List<UtilityOption>();
        double minimumCost = eligible.Min(item => item.GpuMilliseconds);
        foreach (LightingExpertQualification candidate in eligible)
        {
            var state = StateId.Of("lighting.expert." + candidate.Representation);
            float score = (float)(minimumCost / (minimumCost + candidate.GpuMilliseconds));
            options.Add(new(candidate.Representation.ToString(), state, Consideration.Constant(score)));
            graph.Add(state, context => Selected(context, candidate));
        }
        graph.Add(root, Choose);
        var agent = runtime.Add("lighting.expert", new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = false }));
        agent.Bb.Set(sceneKeyFact, sceneKey);
        for (int tick = 0; tick < 4 && agent.Bb.GetOrDefault(selectedKey, "") == ""; tick++)
        {
            runtime.Tick(TimeSpan.FromSeconds(1.0 / 60));
        }
        File.WriteAllText(Path.Combine(output, inspectionName), JsonSerializer.Serialize(runtime.Inspector.Observe(), JsonOptions));
        Require(agent.Bb.GetOrDefault(selectedKey, "") == expected.Representation.ToString(),
            "Kernel utility disagrees with qualified cost selection: " + agent.Bb.GetOrDefault(selectedKey, "not-entered"));
        return expected;

        IEnumerator<AiStep> Choose(AiCtx context)
        {
            yield return new Decide(new DecisionSlot("lighting.representation"), options, new DecisionPolicy(0, 0, 0));
        }

        IEnumerator<AiStep> Selected(AiCtx context, LightingExpertQualification candidate)
        {
            context.Agent.Bb.Set(selectedKey, candidate.Representation.ToString());
            context.Agent.Bb.Set(errorKey, candidate.RootMeanSquareError);
            context.Agent.Bb.Set(costKey, candidate.GpuMilliseconds);
            yield return new Steady("QualifiedSceneExpert");
        }
    }

    internal static CompiledGraphicsProgram Compile(string root, string weights, string output,
        string? choice = null, string? localWeights = null, string? continuousWeights = null, string? sceneDecoder = null)
    {
        var sources = GpuSourceLoader.Load(root, name =>
        {
            if (name == "SceneExpert.v.ts")
            {
                string decoder = sceneDecoder ?? (localWeights is null ? "ExpertDecoder" : "LocalExpertDecoder");
                if (continuousWeights is not null && sceneDecoder is null)
                {
                    decoder = "ContinuousExpertDecoder";
                }
                return "import { PredictLighting } from \"./" + decoder + "\";\n"
                    + "export function SceneLighting(p: float2, mode: f32, sun: f32, lamp: f32): float3 {\n"
                    + "    return PredictLighting(p, mode, sun, lamp);\n}\n";
            }
            if (name == "ContinuousExpertWeights.v.ts")
            {
                return continuousWeights;
            }
            if (name == "LocalExpertWeights.v.ts")
            {
                return localWeights;
            }
            if (name == "ExpertWeights.v.ts")
            {
                return weights;
            }
            if (name == "ExpertChoice.v.ts")
            {
                return choice;
            }
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        });
        var module = GpuGraphicsBinder.Compile(new(sources));
        Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
        Require(!module.Functions.Any(function => function.Name.Contains("Trace", StringComparison.Ordinal)
            || function.Name.Contains("FieldWorld", StringComparison.Ordinal)), "Inference imported geometry traversal.");
        var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
        Require(backend.Vertex.SpirvValidated && backend.Pixel.SpirvValidated, backend.Vertex.DxcOutput + backend.Pixel.DxcOutput);
        File.WriteAllText(Path.Combine(output, Path.GetFileNameWithoutExtension(root) + ".hlsl"), backend.Hlsl);
        foreach (var source in sources)
        {
            File.WriteAllText(Path.Combine(output, Path.GetFileName(source.Path)), source.Source);
        }
        return CompiledGraphicsProgramExporter.Export(module, backend);
    }

    private static string GenerateWeights(SceneLightingExpert expert)
    {
        var source = new StringBuilder("// Generated from a reopened USD stage; scene and decoder identities validated.\n");
        source.AppendLine("import { Add3, Scale3 } from \"./Lighting3D\";");
        float[] parameters = expert.Coefficients("features");
        EmitNetwork("PolynomialRadiance", expert.Coefficients("polynomial"), 6);
        EmitNetwork("NetworkRadiance", expert.Coefficients("network"), SceneLightingExpert.Width);
        EmitNetwork("ResidualRadiance", expert.Coefficients("residual"), SceneLightingExpert.Width);
        float[] grid = expert.Coefficients("grid");
        source.AppendLine("export function GridPoint(cellX: u32, cellZ: u32, channel: u32): float3 {");
        for (int row = 0; row < SceneLightingExpert.GridSize; row++)
        {
            for (int cell = 0; cell < SceneLightingExpert.GridSize; cell++)
            {
                int offset = (row * SceneLightingExpert.GridSize + cell) * 6;
                source.AppendLine($"    if (cellX == {cell} && cellZ == {row}) {{");
                source.AppendLine("        if (channel == 0) {");
                source.AppendLine("            return " + VectorLiteral(grid, offset) + ";");
                source.AppendLine("        }");
                source.AppendLine("        return " + VectorLiteral(grid, offset + 3) + ";");
                source.AppendLine("    }");
            }
        }
        source.AppendLine("    return float3(0.0, 0.0, 0.0);\n}");
        return source.ToString();

        void EmitNetwork(string name, float[] weights, int count)
        {
            source.AppendLine($"export function {name}(p: float2, channel: u32): float3 {{");
            for (int index = 0; index < count; index++)
            {
                string activation = index switch
                {
                    0 => "1.0",
                    1 => "p.x",
                    2 => "p.y",
                    3 => "p.x * p.x",
                    4 => "p.x * p.y",
                    5 => "p.y * p.y",
                    _ => $"Max(p.x * {Literal(parameters[index * 3])} + p.y * {Literal(parameters[index * 3 + 1])} + {Literal(parameters[index * 3 + 2])}, 0.0)",
                };
                source.AppendLine($"    let feature{index}: f32 = {activation};");
            }
            source.AppendLine("    var result: float3 = float3(0.0, 0.0, 0.0);");
            source.AppendLine("    if (channel == 0) {");
            EmitBasis(0);
            source.AppendLine("    } else {");
            EmitBasis(3);
            source.AppendLine("    }");
            source.AppendLine("    return result;\n}");

            void EmitBasis(int channel)
            {
                for (int index = 0; index < count; index++)
                {
                    source.AppendLine($"        result = Add3(result, Scale3({VectorLiteral(weights, index * 6 + channel)}, feature{index}));");
                }
            }
        }

        static string VectorLiteral(float[] values, int offset) =>
            $"float3({Literal(values[offset])}, {Literal(values[offset + 1])}, {Literal(values[offset + 2])})";

        static string Literal(float value)
        {
            string literal = value.ToString("R", CultureInfo.InvariantCulture);
            if (!literal.Contains('.') && !literal.Contains('E'))
            {
                literal += ".0";
            }
            return literal;
        }
    }

    internal static bool[] ReceiverMask(LightingCompilationExperiment.Room room)
    {
        var mask = new bool[TestSize * TestSize];
        for (int index = 0; index < mask.Length; index++)
        {
            Vector2 point = Position(index);
            mask[index] = room.Lighting.Field.EvaluateMetres(new(point.X, .004f, point.Y)) > .001;
        }
        return mask;
    }

    internal static float[] Predict(SceneLightingExpert expert, LightingExpertRepresentation representation)
    {
        var result = new float[TestSize * TestSize * 6];
        for (int index = 0; index < TestSize * TestSize; index++)
        {
            Vector3 sun = expert.Evaluate(Position(index), representation, 1, 0);
            Vector3 lamp = expert.Evaluate(Position(index), representation, 0, 1);
            float[] values = [sun.X, sun.Y, sun.Z, lamp.X, lamp.Y, lamp.Z];
            Array.Copy(values, 0, result, index * 6, 6);
        }
        return result;
    }

    internal static Vector2 Position(int index) => new(
        -2.7f + (index % TestSize + .5f) * 5.4f / TestSize,
        -2.7f + (index / TestSize + .5f) * 5.4f / TestSize);

    internal static Accuracy Compare(float[] expected, float[] actual, bool[] mask, int channelsPerReceiver = 6)
    {
        Require(channelsPerReceiver > 0 && expected.Length == actual.Length
            && expected.Length == mask.Length * channelsPerReceiver, "Measurement packing/extent mismatch.");
        double squared = 0;
        double energy = 0;
        double maximum = 0;
        int count = 0;
        for (int index = 0; index < expected.Length; index++)
        {
            if (!mask[index / channelsPerReceiver])
            {
                continue;
            }
            double error = expected[index] - actual[index];
            squared += error * error;
            energy += expected[index] * expected[index];
            maximum = Math.Max(maximum, Math.Abs(error));
            count++;
        }
        Require(count > 0, "No visible receivers for qualification.");
        return new(Math.Sqrt(squared / count), maximum, Math.Sqrt(squared / Math.Max(energy, 1e-20)));
    }

    internal static int PayloadBytes(LightingExpertRepresentation representation) => representation switch
    {
        LightingExpertRepresentation.Polynomial => 36 * 4,
        LightingExpertRepresentation.CoarseGrid => 216 * 4,
        LightingExpertRepresentation.Neural => (96 + 192) * 4,
        LightingExpertRepresentation.Hybrid => (96 + 192 + 216) * 4,
        _ => throw new ArgumentOutOfRangeException(nameof(representation)),
    };

    private static float[] Read(string path, int count)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Require(bytes.Length == count * 4, "Reference extent mismatch: " + path);
        float[] values = MemoryMarshal.Cast<byte, float>(bytes).ToArray();
        Require(values.All(float.IsFinite), "Nonfinite reference.");
        return values;
    }

    internal static void WritePreview(string output, string name, float[] values, bool[] mask)
    {
        byte[] pixels = new byte[TestSize * TestSize * 4];
        for (int pixel = 0; pixel < TestSize * TestSize; pixel++)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                float radiance = 0;
                if (mask[pixel])
                {
                    radiance = Math.Max(values[pixel * 6 + channel] + values[pixel * 6 + channel + 3], 0);
                }
                float mapped = radiance / (1 + radiance);
                pixels[pixel * 4 + channel] = (byte)Math.Clamp(MathF.Pow(mapped, 1 / 2.2f) * 255, 0, 255);
            }
            pixels[pixel * 4 + 3] = 255;
        }
        NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), TestSize, TestSize, pixels);
    }

    internal static void Sheet(string output, string fileName = "comparison.png", string[]? names = null)
    {
        names ??= ["cycles-reference", "polynomial", "coarsegrid", "neural", "hybrid", "room-view-a", "room-view-b", "room-lamp-off"];
        const int tile = 384;
        using var bitmap = new SKBitmap(tile * 4, (tile + 32) * 2);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(20, 22, 26));
        using var paint = new SKPaint { Color = SKColors.White, TextSize = 20, IsAntialias = true };
        for (int index = 0; index < names.Length; index++)
        {
            int x = index % 4 * tile;
            int y = index / 4 * (tile + 32);
            using var image = SKBitmap.Decode(Path.Combine(output, names[index] + ".png"));
            canvas.DrawText(names[index], x + 10, y + 24, paint);
            canvas.DrawBitmap(image, new SKRect(x, y + 32, x + tile, y + 32 + tile));
        }
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(output, fileName), encoded.ToArray());
    }

    private static void RunBlender(string? executable, string fixture, string output, string decoder, bool reuse,
        bool localExperts, bool constrainedExperts, bool continuousExperts, bool adaptiveExperts)
    {
        executable ??= "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe";
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("Pass --blender with the installed Blender executable; this experiment does not download tools.", executable);
        }
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in new[] { "--background", "--factory-startup", "--python-exit-code", "1", "--python",
            Path.GetFullPath("tools/Aurelian.GraphicsProof/bake-lighting-experts.py"), "--", "--fixture", fixture,
            "--output", output, "--decoder", decoder })
        {
            start.ArgumentList.Add(argument);
        }
        if (reuse)
        {
            start.ArgumentList.Add("--reuse");
        }
        if (localExperts)
        {
            start.ArgumentList.Add("--local-decoder");
            start.ArgumentList.Add(Path.GetFullPath("tools/Aurelian.GraphicsProof/Assets/LocalExpertDecoder.v.ts"));
        }
        if (constrainedExperts)
        {
            start.ArgumentList.Add("--constraints");
            start.ArgumentList.Add(Path.GetFullPath("tools/Aurelian.GraphicsProof/Assets/LightingFitContract.json"));
        }
        if (continuousExperts)
        {
            start.ArgumentList.Add("--continuous-contract");
            start.ArgumentList.Add(Path.GetFullPath("tools/Aurelian.GraphicsProof/Assets/ContinuousFitContract.json"));
        }
        if (adaptiveExperts)
        {
            start.ArgumentList.Add("--adaptive-contract");
            start.ArgumentList.Add(Path.GetFullPath("tools/Aurelian.GraphicsProof/Assets/AdaptiveFitContract.json"));
        }
        Console.WriteLine("Running installed Blender/Cycles/OpenUSD; reference output is captured in blender.log.");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Blender did not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string log = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(output, "blender.log"), log);
        if (log.Contains("REFERENCE basis=", StringComparison.Ordinal))
        {
            File.WriteAllText(Path.Combine(output, "reference-bake.log"), log);
        }
        Require(process.ExitCode == 0 && log.Contains("AURELIAN_USD_EXPERT_ROUNDTRIP_PASSED", StringComparison.Ordinal),
            "Blender reference/artifact failed; inspect " + Path.Combine(output, "blender.log"));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static int ChangedPixels(byte[] first, byte[] second)
    {
        int changed = 0;
        for (int index = 0; index < first.Length; index += 4)
        {
            if (first[index] != second[index] || first[index + 1] != second[index + 1] || first[index + 2] != second[index + 2])
            {
                changed++;
            }
        }
        return changed;
    }

    private static void RequireRejects(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void RequireNoCandidate(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("Invalid-only candidate set was selected.");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
