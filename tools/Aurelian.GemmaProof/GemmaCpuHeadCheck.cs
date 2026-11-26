using System.Text.Json;
using Aurelian.Inference.Gemma4;

internal static class GemmaCpuHeadCheck
{
    public static void Run(Gemma4TextModel model, int[] prompt, int maxTokens, IReadOnlySet<int> stops, string path)
    {
        var cpuScores = new float[Gemma4TextModel.VocabularySize];
        var gpuScores = new float[cpuScores.Length];
        var steps = new List<object>();
        bool winnersMatch = true;
        int[] input = prompt;
        for (int step = 0; step < maxTokens; step++)
        {
            using var hidden = model.Evaluate(input);
            model.UseDeviceGreedyHead = true;
            var gpu = model.GreedyToken(hidden, gpuScores);
            model.UseDeviceGreedyHead = false;
            var cpu = model.GreedyToken(hidden, cpuScores);
            int differences = 0;
            double maxAbsolute = 0;
            int cpuWinner = 0;
            int gpuWinner = 0;
            for (int token = 0; token < cpuScores.Length; token++)
            {
                float left = cpuScores[token];
                float right = gpuScores[token];
                if (!float.IsFinite(left) || !float.IsFinite(right)) { throw new InvalidDataException("Nonfinite score."); }
                if (left > cpuScores[cpuWinner]) { cpuWinner = token; }
                if (right > gpuScores[gpuWinner]) { gpuWinner = token; }
                if (BitConverter.SingleToInt32Bits(left) != BitConverter.SingleToInt32Bits(right))
                {
                    differences++;
                    maxAbsolute = Math.Max(maxAbsolute, Math.Abs((double)left - right));
                }
            }
            if (cpuWinner != cpu.Token || gpuWinner != gpu.Token || cpu.Score != cpuScores[cpuWinner]
                || gpu.Score != gpuScores[gpuWinner])
            {
                throw new InvalidDataException("Greedy selection differs from the complete score buffer.");
            }
            winnersMatch &= cpu.Token == gpu.Token && BitConverter.SingleToInt32Bits(cpu.Score) == BitConverter.SingleToInt32Bits(gpu.Score);
            steps.Add(new { Step = step, CpuToken = cpu.Token, CpuScore = cpu.Score, GpuToken = gpu.Token,
                GpuScore = gpu.Score, DifferentScores = differences, MaxAbsolute = maxAbsolute });
            if (stops.Contains(gpu.Token)) { break; }
            input = [gpu.Token];
        }
        File.WriteAllText(path, JsonSerializer.Serialize(new { Vocabulary = cpuScores.Length, WinnersMatch = winnersMatch, Steps = steps },
            new JsonSerializerOptions { WriteIndented = true }));
        if (!winnersMatch) { throw new InvalidDataException("CPU/GPU greedy disagreement. See " + path); }
        Console.WriteLine("AURELIAN_GEMMA_HEAD_SCORES_CHECKED: " + path);
    }
}
