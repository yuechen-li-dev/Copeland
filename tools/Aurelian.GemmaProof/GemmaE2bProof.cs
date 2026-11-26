using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Aurelian.Graphics.Vulkan.ComputeMath;
using Aurelian.Inference;
using Aurelian.Inference.Dominatus;
using Aurelian.Inference.Gemma4;

internal static class GemmaE2bProof
{
    public static void Run(string[] args)
    {
        if (args.Length < 4)
        {
            throw new ArgumentException("Expected --e2b CHECKPOINT TOKENS_FILE OUTPUT_PREFIX [--optimized], "
                + "or --e2b-generate CHECKPOINT TOKENIZER_JSON PROMPT [MAX_TOKENS].");
        }
        bool generate = args[0] == "--e2b-generate";
        int[][]? sequence = generate ? null : ReadSequence(args[2]);
        int capacity = generate ? 640 : sequence!.Sum(row => row.Length);
        bool optimized = generate || args.Contains("--optimized", StringComparer.Ordinal);
        bool requireCooperative = args.Contains("--cooperative", StringComparer.Ordinal);
        bool cooperative = optimized || requireCooperative;
        using var session = VulkanInferenceSession.Create(new()
        {
            ApplicationName = "Aurelian Gemma 4 E2B",
            MaximumTensorElements = 33554432,
            EnableValidation = false,
            UseCooperativeLinear = cooperative,
        });
        if (requireCooperative && !session.Compute.CooperativeMatrixEnabled)
        {
            throw new InvalidOperationException("Cooperative matrix support is required for this experiment.");
        }
        var timer = Stopwatch.StartNew();
        using var checkpoint = new GgufCheckpoint(args[1]);
        using var model = new Gemma4E2bModel(session.Compute, checkpoint, capacity)
        {
            LayerSubmissions = args.Contains("--layer-submissions", StringComparer.Ordinal),
            PruneSharedRows = !args.Contains("--full-prefill", StringComparer.Ordinal),
            PackedPrefill = optimized || args.Contains("--packed-prefill", StringComparer.Ordinal),
            PackedDecode = optimized || args.Contains("--packed-decode", StringComparer.Ordinal),
            CooperativePrefill = cooperative && session.Compute.CooperativeMatrixEnabled,
            ProfileDispatches = args.Contains("--profile", StringComparer.Ordinal),
            FusedDecodeMaxKeys = args.Contains("--fused-decode", StringComparer.Ordinal) ? 1024 : optimized ? 128 : 0,
            FusedPrefillAttention = optimized || args.Contains("--fused-prefill", StringComparer.Ordinal),
            BoundedNorm = optimized || args.Contains("--bounded-norm", StringComparer.Ordinal),
            DeviceHead = optimized || args.Contains("--device-head", StringComparer.Ordinal),
        };
        double loadMs = timer.Elapsed.TotalMilliseconds;
        Console.WriteLine($"{session.Plant.Facts.PhysicalDeviceName}, loaded in {loadMs:F1} ms");
        if (generate)
        {
            var tokenizer = new HuggingFaceBpeTokenizer(args[2]);
            int[] tokens = tokenizer.Encode(Gemma4Chat.FormatUser(args[3]));
            int count = args.Length > 4 ? int.Parse(args[4]) : 32;
            var generator = new CausalTextGenerator(model, new DominatusComputeSchedule("gemma.e2b.generate"));
            var result = generator.Generate(tokens, count, new HashSet<int> { 1, 106 });
            string raw = tokenizer.Decode(result.Tokens.Select(token => token.Id), skipSpecial: false);
            Console.WriteLine(JsonSerializer.Serialize(new { Result = result, Text = Gemma4Chat.ParseResponse(raw), Raw = raw }));
            return;
        }

        string output = args[3];
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var runs = new List<object>();
        var predictions = new List<int>();
        bool greedy = args.Contains("--greedy", StringComparer.Ordinal);
        for (int run = 0; run < 4; run++)
        {
            model.Reset();
            using var logits = !greedy && run == 1 ? File.Create(output + ".f32") : null;
            var stages = new List<Step>();
            foreach (int[] tokens in sequence!)
            {
                long start = Stopwatch.GetTimestamp();
                using var hidden = model.Evaluate(tokens);
                double elapsed;
                if (greedy)
                {
                    var prediction = model.GreedyToken(hidden);
                    elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    if (run == 1) { predictions.Add(prediction.Token); }
                }
                else
                {
                    ReadOnlySpan<float> scores = model.Logits(hidden);
                    elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    logits?.Write(MemoryMarshal.AsBytes(scores));
                }
                stages.Add(new(elapsed, model.BodyProfile!, model.HeadProfile!));
            }
            Console.WriteLine($"Run {run}: prefill {stages[0].WallMs:F2} ms, decode {stages.Skip(1).Sum(item => item.WallMs):F2} ms");
            if (run > 0) { runs.Add(stages); }
        }
        File.WriteAllText(output + ".json", JsonSerializer.Serialize(new
        {
            Device = session.Plant.Facts.PhysicalDeviceName,
            LoadMs = loadMs,
            PromptLength = sequence![0].Length,
            DecodeSteps = sequence.Length - 1,
            model.LayerSubmissions,
            model.PruneSharedRows,
            model.PackedPrefill,
            model.PackedDecode,
            model.CooperativePrefill,
            model.ProfileDispatches,
            model.FusedDecodeMaxKeys,
            model.FusedPrefillAttention,
            model.BoundedNorm,
            model.DeviceHead,
            Greedy = greedy,
            Predictions = predictions,
            Memory = session.Compute.MemoryTelemetry,
            Runs = runs,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static int[][] ReadSequence(string path)
    {
        int[][] rows = File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray()).ToArray();
        if (rows.Length < 2 || rows.Skip(1).Any(row => row.Length != 1) || rows.Sum(row => row.Length) > 1024)
        {
            throw new InvalidDataException("Expected one prompt line and single-token decode lines, at most 1024 tokens in total.");
        }
        return rows;
    }

    private sealed record Step(double WallMs, Gemma4StageProfile Body, Gemma4StageProfile Head);
}
