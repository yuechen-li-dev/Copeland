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
        long invocationStart = Stopwatch.GetTimestamp();
        if (args.Length < 4)
        {
            throw new ArgumentException("Expected --e2b CHECKPOINT TOKENS_FILE OUTPUT_PREFIX [--optimized], "
                + "or --e2b-generate CHECKPOINT TOKENIZER_JSON PROMPT [MAX_TOKENS].");
        }
        bool generate = args[0] == "--e2b-generate";
        int[][]? sequence = generate ? null : ReadSequence(args[2]);
        int capacity = generate ? 640 : sequence!.Sum(row => row.Length);
        bool optimized = generate || args.Contains("--optimized", StringComparer.Ordinal);
        bool tuned = optimized && !args.Contains("--prior-kernels", StringComparer.Ordinal);
        bool tunedDecode = tuned && !args.Contains("--previous-decode", StringComparer.Ordinal);
        bool requireCooperative = args.Contains("--cooperative", StringComparer.Ordinal);
        bool cooperative = optimized || requireCooperative;
        bool queued = args.Contains("--queued-e2b", StringComparer.Ordinal);
        bool replay = args.Contains("--replay-e2b", StringComparer.Ordinal);
        bool greedy = args.Contains("--greedy", StringComparer.Ordinal);
        bool precompiled = !args.Contains("--compile-shaders-at-runtime", StringComparer.Ordinal);
        if (replay && (!tunedDecode || queued || args.Contains("--profile", StringComparer.Ordinal)))
        {
            throw new ArgumentException("--replay-e2b requires optimized decode without queued execution or per-shader profiling.");
        }
        if (queued && args.Contains("--profile", StringComparer.Ordinal))
        {
            throw new ArgumentException("Use the queue timing report or the synchronous shader profile in separate runs.");
        }
        int decodeLanesOption = Array.IndexOf(args, "--decode-lanes");
        int splitAttentionOption = Array.IndexOf(args, "--split-decode-attention");
        int splitDecodeTileKeys = tunedDecode ? 16 : 0;
        if (splitAttentionOption >= 0 && (splitAttentionOption + 1 >= args.Length
            || !int.TryParse(args[splitAttentionOption + 1], out splitDecodeTileKeys)
            || splitDecodeTileKeys is not (16 or 32 or 64 or 128)))
        {
            throw new ArgumentException("--split-decode-attention requires a 16, 32, 64 or 128-key tile.");
        }
        int decodeLanes = tuned ? 16 : 32;
        if (decodeLanesOption >= 0 && (decodeLanesOption + 1 >= args.Length
            || !int.TryParse(args[decodeLanesOption + 1], out decodeLanes) || decodeLanes is not (8 or 16 or 32)))
        {
            throw new ArgumentException("--decode-lanes requires 8, 16 or 32.");
        }
        int headLayoutOption = Array.IndexOf(args, "--head-layout");
        PackedQ6Layout headLayout = tuned ? PackedQ6Layout.Interleaved8 : PackedQ6Layout.RowMajor;
        if (headLayoutOption >= 0)
        {
            headLayout = (headLayoutOption + 1 < args.Length ? args[headLayoutOption + 1] : "") switch
            {
                "row" => PackedQ6Layout.RowMajor,
                "word" => PackedQ6Layout.RowMajorWord,
                "interleaved" => PackedQ6Layout.Interleaved8,
                _ => throw new ArgumentException("--head-layout requires row, word or interleaved."),
            };
        }
        int headShardRows = tuned && !args.Contains("--narrow-head", StringComparer.Ordinal) ? 65536 : 32768;
        using var session = VulkanInferenceSession.Create(new()
        {
            ApplicationName = "Aurelian Gemma 4 E2B",
            MaximumTensorElements = 33554432,
            EnableValidation = false,
            UseCooperativeLinear = cooperative,
            PushDispatchParameters = optimized && !replay && !args.Contains("--storage-parameters", StringComparer.Ordinal),
            UsePrecompiledShaders = precompiled,
        });
        if (requireCooperative && !session.Compute.CooperativeMatrixEnabled)
        {
            throw new InvalidOperationException("Cooperative matrix support is required for this experiment.");
        }
        session.Compute.MeasureBatchCpuTime = args.Contains("--profile-cpu", StringComparer.Ordinal);
        var pipelineCreationMs = new Dictionary<string, double>(StringComparer.Ordinal);
        if (replay || generate)
        {
            session.Compute.PipelineCreated = (shader, milliseconds) => pipelineCreationMs.Add(shader, milliseconds);
        }
        var timer = Stopwatch.StartNew();
        using var checkpoint = new GgufCheckpoint(args[1]);
        bool interleaved = !args.Contains("--row-major-q4", StringComparer.Ordinal)
            && ((tuned && decodeLanes == 16) || args.Contains("--interleave-q4", StringComparer.Ordinal));
        PackedQ4Layout layout = interleaved ? PackedQ4Layout.Interleaved8 : PackedQ4Layout.RowMajor;
        bool fuseKeyCache = (tunedDecode || args.Contains("--fused-key-cache", StringComparer.Ordinal))
            && !args.Contains("--separate-key-cache", StringComparer.Ordinal);
        bool tunedFeedForward = tunedDecode && interleaved && !args.Contains("--previous-feed-forward", StringComparer.Ordinal);
        bool fuseGateUp = (tunedFeedForward || args.Contains("--fused-gate-up", StringComparer.Ordinal))
            && !args.Contains("--separate-gate-up", StringComparer.Ordinal);
        int downTilesOption = Array.IndexOf(args, "--down-tiles");
        int downProjectionTiles = tunedFeedForward ? 2 : 0;
        if (downTilesOption >= 0 && (downTilesOption + 1 >= args.Length
            || !int.TryParse(args[downTilesOption + 1], out downProjectionTiles) || downProjectionTiles is not (0 or 1 or 2 or 4)))
        {
            throw new ArgumentException("--down-tiles requires 0, 1, 2 or 4.");
        }
        bool mergeDownNormalization = (tunedFeedForward && downProjectionTiles >= 2
            || args.Contains("--merge-down-norm", StringComparer.Ordinal))
            && !args.Contains("--separate-down-norm", StringComparer.Ordinal);
        using var model = new Gemma4E2bModel(session.Compute, checkpoint, capacity, layout, fuseKeyCache, headLayout,
            headShardRows, fuseGateUp, fuseQueryRope: tunedDecode && !args.Contains("--separate-query-rope", StringComparer.Ordinal))
        {
            LayerSubmissions = args.Contains("--layer-submissions", StringComparer.Ordinal),
            PruneSharedRows = !args.Contains("--full-prefill", StringComparer.Ordinal),
            PackedPrefill = optimized || args.Contains("--packed-prefill", StringComparer.Ordinal),
            PackedDecode = optimized || args.Contains("--packed-decode", StringComparer.Ordinal),
            PackedDecodeLanes = decodeLanes,
            DownProjectionTiles = downProjectionTiles,
            MergeDownNormalization = mergeDownNormalization,
            CooperativePrefill = cooperative && session.Compute.CooperativeMatrixEnabled,
            ReusePrefillInputPacking = tuned || args.Contains("--reuse-prefill-pack", StringComparer.Ordinal),
            ProfileDispatches = args.Contains("--profile", StringComparer.Ordinal),
            BatchInputUpload = (tuned || args.Contains("--batch-input-upload", StringComparer.Ordinal))
                && !args.Contains("--separate-input-uploads", StringComparer.Ordinal),
            FusedDecodeMaxKeys = args.Contains("--fused-decode", StringComparer.Ordinal) ? 1024 : optimized ? 128 : 0,
            SplitDecodeTileKeys = splitDecodeTileKeys,
            FusedPrefillAttention = optimized || args.Contains("--fused-prefill", StringComparer.Ordinal),
            WidePrefillKeyTile = tuned || args.Contains("--wide-prefill-attention", StringComparer.Ordinal),
            BoundedNorm = optimized || args.Contains("--bounded-norm", StringComparer.Ordinal),
            SpecializeDecodeNorm = tunedDecode || args.Contains("--specialize-decode-norm", StringComparer.Ordinal),
            DeviceHead = optimized || args.Contains("--device-head", StringComparer.Ordinal),
            ReplayGreedyDecode = replay && (generate || greedy),
        };
        double loadMs = timer.Elapsed.TotalMilliseconds;
        Console.WriteLine($"{session.Plant.Facts.PhysicalDeviceName}, loaded in {loadMs:F1} ms");
        if (generate)
        {
            var tokenizer = new HuggingFaceBpeTokenizer(args[2]);
            int[] tokens = args.Contains("--prompt-token-file", StringComparer.Ordinal)
                ? ReadSequence(args[3])[0]
                : tokenizer.Encode(Gemma4Chat.FormatUser(args[3]));
            int count = args.Length > 4 && !args[4].StartsWith("--", StringComparison.Ordinal) ? int.Parse(args[4]) : 32;
            int runsOption = Array.IndexOf(args, "--generation-runs");
            int repeatCount = 1;
            if (runsOption >= 0 && (runsOption + 1 >= args.Length
                || !int.TryParse(args[runsOption + 1], out repeatCount) || repeatCount < 1))
            {
                throw new ArgumentException("--generation-runs requires a positive count.");
            }
            var generator = new CausalTextGenerator(model, new DominatusComputeSchedule("gemma.e2b.generate"));
            var stops = new HashSet<int> { 1, 106 };
            var passes = new List<GenerationPass>();
            for (int run = 0; run < repeatCount; run++)
            {
                using var queue = queued ? BeginQueue(session.Compute, model, capacity, Math.Min(tokens.Length, capacity)) : null;
                int firstReplay = model.ReplayedTokens;
                double startMs = Stopwatch.GetElapsedTime(invocationStart).TotalMilliseconds;
                var generated = generator.Generate(tokens, count, stops);
                passes.Add(new(generated, startMs, model.ReplayedTokens - firstReplay));
            }
            var result = passes[0].Result;
            string raw = tokenizer.Decode(result.Tokens.Select(token => token.Id), skipSpecial: false);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Result = result,
                Text = Gemma4Chat.ParseResponse(raw),
                Raw = raw,
                PromptTokens = tokens.Length,
                LoadMs = loadMs,
                ColdFirstTokenMs = passes[0].StartedMs + result.TimeToFirstTokenMilliseconds,
                Queued = queued,
                Replay = replay,
                session.Compute.PushDispatchParameters,
                PrecompiledShaders = precompiled,
                model.ReplayPreparationMs,
                PipelineCreationMs = pipelineCreationMs,
                Runs = passes,
                Memory = session.Compute.MemoryTelemetry,
            }));
            return;
        }

        string output = args[3];
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var runs = new List<object>();
        var queueRuns = new List<ComputeQueueMetrics>();
        var predictions = new List<int>();
        for (int run = 0; run < 4; run++)
        {
            model.Reset();
            using var queue = queued ? BeginQueue(session.Compute, model, sequence!.Length, sequence[0].Length) : null;
            using var logits = !greedy && run == 1 ? File.Create(output + ".f32") : null;
            var stages = new List<Step>();
            foreach (int[] tokens in sequence!)
            {
                long start = Stopwatch.GetTimestamp();
                long allocated = session.Compute.MeasureBatchCpuTime ? GC.GetAllocatedBytesForCurrentThread() : 0;
                double elapsed;
                if (replay && tokens.Length == 1 && greedy && model.TryDecodeGreedy(tokens[0], out var replayPrediction))
                {
                    elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    if (run == 1) { predictions.Add(replayPrediction.Token); }
                }
                else if (replay && tokens.Length == 1 && !greedy)
                {
                    ReadOnlySpan<float> scores = model.DecodeLogitsWithReplay(tokens[0]);
                    elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    logits?.Write(MemoryMarshal.AsBytes(scores));
                }
                else
                {
                    using var hidden = model.Evaluate(tokens);
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
                }
                long? managedBytes = session.Compute.MeasureBatchCpuTime
                    ? GC.GetAllocatedBytesForCurrentThread() - allocated : null;
                stages.Add(new(elapsed, model.BodyProfile!, model.HeadProfile!, managedBytes));
            }
            Console.WriteLine($"Run {run}: prefill {stages[0].WallMs:F2} ms, decode {stages.Skip(1).Sum(item => item.WallMs):F2} ms");
            if (run > 0)
            {
                runs.Add(stages);
                if (queue is not null)
                {
                    queueRuns.Add(queue.Metrics);
                }
            }
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
            model.PackedDecodeLanes,
            Q4Layout = model.Q4Layout.ToString(),
            model.FuseKeyCache,
            model.FuseGateUp,
            model.DownProjectionTiles,
            model.MergeDownNormalization,
            model.CooperativePrefill,
            session.Compute.PushDispatchParameters,
            model.ReusePrefillInputPacking,
            model.ProfileDispatches,
            model.BatchInputUpload,
            session.Compute.MeasureBatchCpuTime,
            model.FusedDecodeMaxKeys,
            model.SplitDecodeTileKeys,
            model.FuseQueryRope,
            model.FusedPrefillAttention,
            model.WidePrefillKeyTile,
            model.BoundedNorm,
            model.SpecializeDecodeNorm,
            model.DeviceHead,
            HeadLayout = model.HeadLayout.ToString(),
            model.HeadShardRows,
            Queued = queued,
            Replay = replay,
            model.ReplayPreparationMs,
            model.ReplayedTokens,
            PipelineCreationMs = pipelineCreationMs,
            QueueRuns = queueRuns,
            Greedy = greedy,
            Predictions = predictions,
            Memory = session.Compute.MemoryTelemetry,
            Runs = runs,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed record GenerationPass(TextGenerationResult Result, double StartedMs, int ReplayedTokens);

    private static VulkanComputeMath.QueuedComputeScope BeginQueue(VulkanComputeMath compute,
        Gemma4E2bModel model, int positions, int promptRows)
    {
        int batches = model.LayerSubmissions ? positions * 38 : positions * 4 + 8;
        ulong promptBytes = (ulong)promptRows * (ulong)Gemma4E2bModel.InputUploadBytesPerToken;
        return compute.BeginQueuedScope(new ThreeSlots(), new(batches, compute.MaximumDispatches, promptBytes));
    }

    private sealed class ThreeSlots : IComputeStreamController
    {
        public int Plan(ComputeStreamForecast forecast) => 3;
        public void Observe(ComputeStreamObservation observation) { }
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

    private sealed record Step(double WallMs, Gemma4StageProfile Body, Gemma4StageProfile Head, long? ManagedBytes);
}
