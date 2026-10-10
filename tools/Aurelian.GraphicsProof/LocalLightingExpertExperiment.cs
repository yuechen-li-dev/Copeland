using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aurelian.Games;
using Aurelian.GameHost.Silk;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Rendering.Contracts.Lighting;
using Aurelian.Rendering.Contracts.Shaders;
using static LightingExpertExperiment;

internal static class LocalLightingExpertExperiment
{
    private const int TestSize = 64;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record Result(LightingExpertRepresentation Representation, Accuracy Accuracy,
        Accuracy BoundaryAccuracy, Accuracy CpuGpuAgreement, double GpuMilliseconds, int CoefficientBytes);

    public static void Run(string output, LightingCompilationExperiment.Room room, AurelianVulkanPlant plant,
        SceneLightingExpert global, string globalWeights, float[] reference, float[] repeat, bool[] visible)
    {
        string decoderPath = Path.GetFullPath("tools/Aurelian.GraphicsProof/Assets/LocalExpertDecoder.v.ts");
        string decoderKey = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(decoderPath))).ToLowerInvariant();
        byte[] manifest = File.ReadAllBytes(Path.Combine(output, "loaded-local-expert.json"));
        SceneLocalLightingExpert local = SceneLocalLightingExpert.Load(manifest, global.SceneKey, decoderKey);
        RequireRejected(() => SceneLocalLightingExpert.Load(manifest, "changed-scene", decoderKey));
        RequireRejected(() => SceneLocalLightingExpert.Load(manifest, global.SceneKey, "changed-decoder"));
        string altered = Encoding.UTF8.GetString(manifest).Replace(local.WeightsKey, new string('0', 64), StringComparison.Ordinal);
        RequireRejected(() => SceneLocalLightingExpert.Load(Encoding.UTF8.GetBytes(altered), global.SceneKey, decoderKey));
        bool[] boundary = BoundaryMask(room, visible);
        Require(boundary.Count(value => value) > 100, "BRep visibility boundary set is too small.");
        string localDirectory = Path.Combine(output, "local-decoder");
        string baselineDirectory = Path.Combine(output, "baseline-decoder");
        Directory.CreateDirectory(localDirectory);
        Directory.CreateDirectory(baselineDirectory);
        string localWeights = GenerateWeights(local);
        CompiledGraphicsProgram localProbe = Compile("ExpertProbe.v.ts", globalWeights, localDirectory, localWeights: localWeights);
        CompiledGraphicsProgram baselineProbe = Compile("ExpertProbe.v.ts", globalWeights, baselineDirectory);
        var results = new List<Result>();
        LightingExpertRepresentation[] representations =
        [
            LightingExpertRepresentation.Neural, LightingExpertRepresentation.Hybrid,
            LightingExpertRepresentation.GeometryLocal, LightingExpertRepresentation.UniformLocal,
            LightingExpertRepresentation.MatchedGrid,
        ];
        foreach (LightingExpertRepresentation representation in representations)
        {
            bool isLocal = representation >= LightingExpertRepresentation.GeometryLocal;
            CompiledGraphicsProgram shader = isLocal ? localProbe : baselineProbe;
            string weightsKey = isLocal ? local.WeightsKey : global.WeightsKey;
            float[] expected = isLocal ? Predict(local, representation) : LightingExpertExperiment.Predict(global, representation);
            float[] actual = new float[reference.Length];
            for (int basis = 0; basis < 2; basis++)
            {
                using var batch = Infer(plant, shader, global.SceneKey, weightsKey, representation, basis, TestSize);
                float[] pixels = batch.ReadLinear();
                Require(batch.IsQualified, "Local decoder returned unresolved output.");
                for (int pixel = 0; pixel < TestSize * TestSize; pixel++)
                {
                    Array.Copy(pixels, pixel * 4, actual, pixel * 6 + basis * 3, 3);
                }
            }
            Accuracy agreement = Compare(expected, actual, Enumerable.Repeat(true, visible.Length).ToArray());
            Require(agreement.MaximumError < .001, "Local CPU/Vulkan mismatch: " + agreement);
            var times = new List<double>();
            for (int measurement = 0; measurement < 7; measurement++)
            {
                using var batch = Infer(plant, shader, global.SceneKey, weightsKey, representation, 2, 256);
                if (measurement >= 2)
                {
                    times.Add(batch.GpuMilliseconds());
                }
            }
            results.Add(new(representation, Compare(reference, actual, visible), Compare(reference, actual, boundary),
                agreement, times.Order().ElementAt(2), PayloadBytes(representation)));
            WritePreview(output, representation.ToString().ToLowerInvariant(), actual, visible);
            WriteError(output, representation.ToString().ToLowerInvariant() + "-error", reference, actual, visible);
        }
        Result geometry = results.Single(item => item.Representation == LightingExpertRepresentation.GeometryLocal);
        Result uniform = results.Single(item => item.Representation == LightingExpertRepresentation.UniformLocal);
        Result grid = results.Single(item => item.Representation == LightingExpertRepresentation.MatchedGrid);
        Result hybrid = results.Single(item => item.Representation == LightingExpertRepresentation.Hybrid);
        bool lowerOverall = geometry.Accuracy.Rmse < hybrid.Accuracy.Rmse
            && geometry.Accuracy.Rmse < uniform.Accuracy.Rmse && geometry.Accuracy.Rmse < grid.Accuracy.Rmse;
        bool lowerBoundary = geometry.BoundaryAccuracy.Rmse < hybrid.BoundaryAccuracy.Rmse
            && geometry.BoundaryAccuracy.Rmse < uniform.BoundaryAccuracy.Rmse && geometry.BoundaryAccuracy.Rmse < grid.BoundaryAccuracy.Rmse;
        // The example policy gates independently measured boundary error before kernel utility.
        var candidates = results.Where(item => item.BoundaryAccuracy.Rmse <= .01)
            .Select(item => new LightingExpertQualification(global.SceneKey, item.Representation,
                item.Accuracy.Rmse, item.GpuMilliseconds)).ToList();
        candidates.Add(new("stale-scene", LightingExpertRepresentation.GeometryLocal, 0, .000001));
        LightingExpertQualification selected = SceneLightingExpert.Select(candidates, global.SceneKey, .004);
        SelectWithDominatus(candidates, global.SceneKey, .004, selected, output, "local-expert-inspection.json");
        string choice = "export function ExpertChoice(): f32 {\n    return " + ((int)selected.Representation) + ".0;\n}\n";
        bool selectedLocal = selected.Representation >= LightingExpertRepresentation.GeometryLocal;
        CompiledGraphicsProgram roomShader = Compile("ExpertSolid3D.v.ts", globalWeights, localDirectory, choice,
            selectedLocal ? localWeights : null);
        Vector3 sun = Vector3.Normalize(new Vector3(.6f, 1, .4f));
        byte[] first = RenderRoom(output, room, plant, roomShader, sun, new(7, 6, 9), "local-room-a", 1);
        byte[] other = RenderRoom(output, room, plant, roomShader, sun, new(-5, 4, 7), "local-room-b", 1);
        byte[] lampOff = RenderRoom(output, room, plant, roomShader, sun, new(7, 6, 9), "local-lamp-off", 0);
        int changed = ChangedPixels(first, lampOff);
        Require(changed > 100 && ChangedPixels(first, other) > 100, "Local decoder camera/light variants did not change visible pixels.");
        Vector2 query = new(-1, 1);
        foreach (LightingExpertRepresentation representation in representations.Where(item => item >= LightingExpertRepresentation.GeometryLocal))
        {
            Vector3 combination = local.Evaluate(query, representation, 2, .3f);
            Vector3 explicitSum = local.Evaluate(query, representation, 1, 0) * 2 + local.Evaluate(query, representation, 0, 1) * .3f;
            Require(Vector3.Distance(combination, explicitSum) < .000001f, "Local basis scaling is not linear.");
        }
        WritePartitions(output, local, visible, boundary);
        Sheet(output, "local-comparison.png",
            ["cycles-reference", "hybrid", "uniformlocal", "geometrylocal", "matchedgrid", "partitions", "boundary-mask", "local-room-a"]);
        Sheet(output, "local-errors.png",
            ["neural-error", "hybrid-error", "uniformlocal-error", "geometrylocal-error", "matchedgrid-error", "partitions", "boundary-mask", "local-room-b"]);
        object seams = SeamProbe(local, room);
        File.WriteAllText(Path.Combine(output, "local-expert-evidence.json"), JsonSerializer.Serialize(new
        {
            Accepted = true,
            Device = plant.Facts.PhysicalDeviceName,
            local.SceneKey,
            local.DecoderKey,
            local.WeightsKey,
            NativeUsdRoundTrip = true,
            TrainingReceivers = 48 * 48,
            VisibleHeldOutReceivers = visible.Count(value => value),
            BoundaryReceivers = boundary.Count(value => value),
            SamplingNoise = Compare(reference, repeat, visible),
            BoundarySamplingNoise = Compare(reference, repeat, boundary),
            BoundaryDefinition = "BRep lamp visibility changes across +/-0.12m receiver offsets at centre and four emitter corners; or offsets enter the occluder",
            GeometryHasLowerOverallRmse = lowerOverall,
            GeometryHasLowerBoundaryRmse = lowerBoundary,
            Results = results.Select(item => new
            {
                Representation = item.Representation.ToString(), item.Accuracy, item.BoundaryAccuracy,
                item.CpuGpuAgreement, item.GpuMilliseconds, item.CoefficientBytes,
            }),
            Seams = seams,
            Selected = selected.Representation.ToString(),
            OverallRmseGate = .004,
            BoundaryRmseGate = .01,
            LampOffChangedPixels = changed,
            RuntimeGeometryQueries = 0,
            RuntimeTraining = false,
            StaleSceneDecoderAndChecksumRejected = true,
            LinearLightCoefficients = true,
            PartitionAuthoringHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, "partition-authoring.json")))).ToLowerInvariant(),
            Limits = new[]
            {
                "Single static floor and fixed diffuse materials; no scene generalization or dynamic geometry qualification",
                "Geometry candidates use this box/emitter's authored bounds; not a general BRep silhouette compiler",
                "Hard local partitions have no continuity constraints; seam probe reports remaining jumps",
                "Grid coefficient payload is slightly larger; it uses explicit constant cell selection rather than a hardware texture",
                "Finite 8192-sample/eight-bounce Cycles reference; GPU timestamps exclude pipeline/submission/readback",
            },
        }, JsonOptions));
        Console.WriteLine("AURELIAN_LOCAL_LIGHTING_EXPERTS_PASSED " + plant.Facts.PhysicalDeviceName);
    }

    private static float[] Predict(SceneLocalLightingExpert expert, LightingExpertRepresentation representation)
    {
        var result = new float[TestSize * TestSize * 6];
        for (int pixel = 0; pixel < TestSize * TestSize; pixel++)
        {
            Vector3 sun = expert.Evaluate(Position(pixel), representation, 1, 0);
            Vector3 lamp = expert.Evaluate(Position(pixel), representation, 0, 1);
            float[] values = [sun.X, sun.Y, sun.Z, lamp.X, lamp.Y, lamp.Z];
            Array.Copy(values, 0, result, pixel * 6, 6);
        }
        return result;
    }

    private static int PayloadBytes(LightingExpertRepresentation representation) => representation switch
    {
        LightingExpertRepresentation.GeometryLocal => (21 + 14 + 32 + 480) * 4,
        LightingExpertRepresentation.UniformLocal => (32 + 480) * 4,
        LightingExpertRepresentation.MatchedGrid => 600 * 4,
        _ => LightingExpertExperiment.PayloadBytes(representation),
    };

    private static bool[] BoundaryMask(LightingCompilationExperiment.Room room, bool[] visible)
    {
        var block = room.Bodies.Single(body => body.Declaration.Id == "blue-block");
        var occluderRoom = room with { Bodies = [block] };
        Vector3[] lampVertices = LightingCompilationExperiment.Geometry(room.Bodies.Single(body => body.Declaration.Id == "lamp"))
            .Select(vertex => vertex.Position).ToArray();
        Vector3 lower = lampVertices.Aggregate(Vector3.Min);
        Vector3 upper = lampVertices.Aggregate(Vector3.Max);
        float z = (lower.Z + upper.Z) * .5f;
        Vector3[] lights = [(lower + upper) * .5f, new(lower.X, lower.Y, z), new(lower.X, upper.Y, z),
            new(upper.X, lower.Y, z), new(upper.X, upper.Y, z)];
        Vector2[] offsets = [new(.12f, 0), new(-.12f, 0), new(0, .12f), new(0, -.12f)];
        var mask = new bool[visible.Length];
        for (int pixel = 0; pixel < visible.Length; pixel++)
        {
            if (!visible[pixel])
            {
                continue;
            }
            Vector2 point = Position(pixel);
            foreach (Vector2 offset in offsets)
            {
                Vector2 neighbour = point + offset;
                if (Math.Abs(neighbour.X) > 2.7f || Math.Abs(neighbour.Y) > 2.7f)
                {
                    continue;
                }
                if (room.Lighting.Field.EvaluateMetres(new(neighbour.X, .004f, neighbour.Y)) < .001
                    || lights.Any(light => Blocked(point, light) != Blocked(neighbour, light)))
                {
                    mask[pixel] = true;
                    break;
                }
            }
        }
        return mask;

        bool Blocked(Vector2 point, Vector3 light)
        {
            Vector3 origin = new(point.X, .004f, point.Y);
            Vector3 direction = Vector3.Normalize(light - origin);
            var hit = LightingCompilationExperiment.Closest(occluderRoom, origin, direction);
            return hit is not null && hit.Distance < Vector3.Distance(light, origin) * 1000;
        }
    }

    private static object SeamProbe(SceneLocalLightingExpert expert, LightingCompilationExperiment.Room room)
    {
        float[] planes = expert.Coefficients("planes");
        double maximum = 0;
        int pairs = 0;
        // Probe the actual selected splits across a 1mm total separation, independently of reference labels.
        for (int node = 0; node < SceneLocalLightingExpert.Leaves - 1; node++)
        {
            Vector2 normal = new(planes[node * 3], planes[node * 3 + 1]);
            Vector2 tangent = new(-normal.Y, normal.X);
            for (int sample = 0; sample < 256; sample++)
            {
                Vector2 centre = normal * -planes[node * 3 + 2] + tangent * (-1.5f + (sample + .5f) * 3 / 256);
                Vector2 first = centre - normal * (.0005f / 2.7f);
                Vector2 second = centre + normal * (.0005f / 2.7f);
                if (Math.Abs(first.X) > 1 || Math.Abs(first.Y) > 1 || Math.Abs(second.X) > 1 || Math.Abs(second.Y) > 1
                    || expert.Route(first) == expert.Route(second))
                {
                    continue;
                }
                Vector2 firstMetres = first * 2.7f;
                Vector2 secondMetres = second * 2.7f;
                if (room.Lighting.Field.EvaluateMetres(new(firstMetres.X, .004f, firstMetres.Y)) < .001
                    || room.Lighting.Field.EvaluateMetres(new(secondMetres.X, .004f, secondMetres.Y)) < .001)
                {
                    continue;
                }
                Vector3 difference = Vector3.Abs(expert.Evaluate(firstMetres, LightingExpertRepresentation.GeometryLocal)
                    - expert.Evaluate(secondMetres, LightingExpertRepresentation.GeometryLocal));
                maximum = Math.Max(maximum, Math.Max(difference.X, Math.Max(difference.Y, difference.Z)));
                pairs++;
            }
        }
        return new { Pairs = pairs, TotalSeparationMetres = .001, MaximumCombinedRgbChange = maximum,
            Scope = "Decoder jump diagnostic, not comparison with independently baked 1mm reference samples" };
    }

    private static void WriteError(string output, string name, float[] reference, float[] prediction, bool[] visible)
    {
        var pixels = new byte[TestSize * TestSize * 4];
        for (int pixel = 0; pixel < visible.Length; pixel++)
        {
            float error = 0;
            for (int channel = 0; channel < 6; channel++)
            {
                error = Math.Max(error, Math.Abs(reference[pixel * 6 + channel] - prediction[pixel * 6 + channel]));
            }
            byte value = visible[pixel] ? (byte)Math.Clamp(error / .05f * 255, 0, 255) : (byte)0;
            pixels[pixel * 4] = value;
            pixels[pixel * 4 + 1] = (byte)(value / 3);
            pixels[pixel * 4 + 3] = 255;
        }
        NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), TestSize, TestSize, pixels);
    }

    private static void WritePartitions(string output, SceneLocalLightingExpert expert, bool[] visible, bool[] boundary)
    {
        byte[][] colours = [[225, 105, 85], [72, 170, 130], [100, 140, 220], [225, 190, 70],
            [175, 100, 200], [55, 185, 200], [205, 135, 70], [130, 160, 80]];
        var pixels = new byte[TestSize * TestSize * 4];
        var mask = new byte[pixels.Length];
        for (int pixel = 0; pixel < visible.Length; pixel++)
        {
            if (visible[pixel])
            {
                byte[] colour = colours[expert.Route(Position(pixel) / 2.7f)];
                Array.Copy(colour, 0, pixels, pixel * 4, 3);
                byte value = boundary[pixel] ? (byte)255 : (byte)40;
                mask[pixel * 4] = value;
                mask[pixel * 4 + 1] = value;
                mask[pixel * 4 + 2] = value;
            }
            pixels[pixel * 4 + 3] = 255;
            mask[pixel * 4 + 3] = 255;
        }
        NativeGameGraphics.WritePng(Path.Combine(output, "partitions.png"), TestSize, TestSize, pixels);
        NativeGameGraphics.WritePng(Path.Combine(output, "boundary-mask.png"), TestSize, TestSize, mask);
    }

    private static string GenerateWeights(SceneLocalLightingExpert expert)
    {
        var source = new StringBuilder("// Generated from native-reloaded USD; validated routing and coefficients.\n");
        source.AppendLine("import { Add3, Scale3 } from \"./Lighting3D\";");
        EmitLeaves("Geometry", expert.Coefficients("transforms"), expert.Coefficients("weights"));
        EmitLeaves("Uniform", expert.Coefficients("uniformTransforms"), expert.Coefficients("uniformWeights"));
        float[] planes = expert.Coefficients("planes");
        int[] children = expert.Children();
        source.AppendLine("export function GeometryRadiance(p: float2, channel: u32): float3 {");
        EmitNode(0, "    ");
        source.AppendLine("}");
        source.AppendLine("export function UniformRadiance(p: float2, channel: u32): float3 {");
        for (int row = 0; row < 2; row++)
        {
            if (row == 0)
            {
                source.AppendLine("    if (p.y < 0.0) {");
            }
            else
            {
                source.AppendLine("    } else {");
            }
            for (int column = 0; column < 3; column++)
            {
                source.AppendLine($"        if (p.x < {Literal(-.5f + column * .5f)}) {{");
                source.AppendLine($"            return UniformLeaf{row * 4 + column}(p, channel);\n        }}");
            }
            source.AppendLine($"        return UniformLeaf{row * 4 + 3}(p, channel);");
        }
        source.AppendLine("    }\n}");
        float[] grid = expert.Coefficients("grid");
        source.AppendLine("export function LocalGridPoint(cellX: u32, cellZ: u32, channel: u32): float3 {");
        for (int row = 0; row < SceneLocalLightingExpert.GridSize; row++)
        {
            for (int column = 0; column < SceneLocalLightingExpert.GridSize; column++)
            {
                int offset = (row * SceneLocalLightingExpert.GridSize + column) * 6;
                source.AppendLine($"    if (cellX == {column} && cellZ == {row}) {{");
                source.AppendLine("        if (channel == 0) {\n            return " + VectorLiteral(grid, offset) + ";\n        }");
                source.AppendLine("        return " + VectorLiteral(grid, offset + 3) + ";\n    }");
            }
        }
        source.AppendLine("    return float3(0.0, 0.0, 0.0);\n}");
        return source.ToString();

        void EmitNode(int node, string indent)
        {
            if (node < 0)
            {
                source.AppendLine(indent + $"return GeometryLeaf{-node - 1}(p, channel);");
                return;
            }
            int offset = node * 3;
            source.AppendLine(indent + $"if (p.x * {Literal(planes[offset])} + p.y * {Literal(planes[offset + 1])} + {Literal(planes[offset + 2])} <= 0.0) {{");
            EmitNode(children[node * 2], indent + "    ");
            source.AppendLine(indent + "} else {");
            EmitNode(children[node * 2 + 1], indent + "    ");
            source.AppendLine(indent + "}");
        }

        void EmitLeaves(string prefix, float[] transforms, float[] weights)
        {
            for (int leaf = 0; leaf < SceneLocalLightingExpert.Leaves; leaf++)
            {
                source.AppendLine($"function {prefix}Leaf{leaf}(p: float2, channel: u32): float3 {{");
                int offset = leaf * 4;
                source.AppendLine($"    let x: f32 = (p.x - {Literal(transforms[offset])}) * {Literal(transforms[offset + 2])};");
                source.AppendLine($"    let z: f32 = (p.y - {Literal(transforms[offset + 1])}) * {Literal(transforms[offset + 3])};");
                string[] features = ["1.0", "x", "z", "x * x", "x * z", "z * z", "x * x * x", "x * x * z", "x * z * z", "z * z * z"];
                for (int feature = 0; feature < features.Length; feature++)
                {
                    source.AppendLine($"    let feature{feature}: f32 = {features[feature]};");
                }
                source.AppendLine("    var result: float3 = float3(0.0, 0.0, 0.0);\n    if (channel == 0) {");
                EmitBasis(0);
                source.AppendLine("    } else {");
                EmitBasis(3);
                source.AppendLine("    }\n    return result;\n}");

                void EmitBasis(int channel)
                {
                    for (int feature = 0; feature < SceneLocalLightingExpert.Features; feature++)
                    {
                        int weight = (leaf * SceneLocalLightingExpert.Features + feature) * 6 + channel;
                        source.AppendLine($"        result = Add3(result, Scale3({VectorLiteral(weights, weight)}, feature{feature}));");
                    }
                }
            }
        }
    }

    private static string VectorLiteral(float[] values, int offset) =>
        $"float3({Literal(values[offset])}, {Literal(values[offset + 1])}, {Literal(values[offset + 2])})";

    private static string Literal(float value)
    {
        string literal = value.ToString("R", CultureInfo.InvariantCulture);
        if (!literal.Contains('.') && !literal.Contains('E'))
        {
            literal += ".0";
        }
        return literal;
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
        throw new InvalidOperationException("Local artifact invalidation was bypassed.");
    }

    private static void Require(bool condition, string message) => LightingExpertExperiment.Require(condition, message);
}
