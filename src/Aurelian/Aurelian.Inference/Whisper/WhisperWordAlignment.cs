using System.Text;

namespace Aurelian.Inference.Whisper;

internal static class WhisperWordAlignment
{
    private const int SamplesPerFrame = 320;
    private const int FilterRadius = 3;

    internal static IReadOnlyList<WhisperWordTiming> Align(TinyEnglishCheckpoint checkpoint,
        IReadOnlyList<int> tokens, float[] attention, float[] pcm, int pcmOffset, int sampleCount, CancellationToken cancellation)
    {
        if (tokens.Count == 0)
        {
            return [];
        }
        int frames = (sampleCount + SamplesPerFrame - 1) / SamplesPerFrame;
        // Each DTW transition marks the end of the preceding token. The leading
        // no-timestamps row gives the first text token its initial range.
        var starts = TokenStarts(attention, tokens.Count + 1, frames, cancellation);
        var bytes = new List<byte>();
        var boundaries = new int[tokens.Count + 1];
        for (int token = 0; token < tokens.Count; token++)
        {
            boundaries[token] = bytes.Count;
            foreach (byte value in checkpoint.TextBytes(tokens[token]))
            {
                bytes.Add(value);
            }
        }
        boundaries[^1] = bytes.Count;
        byte[] utf8 = bytes.ToArray();
        var words = new List<(string Text, int Start)>();
        int currentByte = 0;
        int currentToken = 0;
        int wordByte = -1;
        foreach (var rune in new UTF8Encoding(false, true).GetString(utf8).EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                FinishWord(currentByte);
            }
            else if (wordByte < 0)
            {
                wordByte = currentByte;
            }
            currentByte += rune.Utf8SequenceLength;
        }
        FinishWord(utf8.Length);

        var output = new List<WhisperWordTiming>(words.Count);
        for (int word = 0; word < words.Count; word++)
        {
            var (text, start) = words[word];
            int end = word + 1 < words.Count ? words[word + 1].Start : sampleCount;
            var timing = TrimSilence(pcm, pcmOffset, text, start, end);
            output.Add(timing with
            {
                FirstSample = timing.FirstSample + pcmOffset,
                EndSample = timing.EndSample + pcmOffset,
                UnrefinedFirstSample = timing.UnrefinedFirstSample + pcmOffset,
                UnrefinedEndSample = timing.UnrefinedEndSample + pcmOffset,
            });
        }
        return output;

        void FinishWord(int end)
        {
            if (wordByte < 0)
            {
                return;
            }
            while (currentToken + 1 < tokens.Count && boundaries[currentToken + 1] <= wordByte)
            {
                currentToken++;
            }
            words.Add((Encoding.UTF8.GetString(utf8, wordByte, end - wordByte),
                Math.Min(sampleCount, starts[currentToken] * SamplesPerFrame)));
            wordByte = -1;
        }
    }

    private static int[] TokenStarts(float[] attention, int tokens, int frames, CancellationToken cancellation)
    {
        const int stride = TinyEnglishModel.AlignmentHeadCount * TinyEnglishCheckpoint.AudioRows;
        var cost = new float[tokens * frames];
        var normalized = new float[cost.Length];
        Span<float> window = stackalloc float[2 * FilterRadius + 1];
        for (int head = 0; head < TinyEnglishModel.AlignmentHeadCount; head++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int frame = 0; frame < frames; frame++)
            {
                int entry = head * TinyEnglishCheckpoint.AudioRows + frame;
                double sum = 0, squares = 0;
                for (int token = 0; token < tokens; token++)
                {
                    float value = attention[token * stride + entry];
                    sum += value;
                    squares += (double)value * value;
                }
                double mean = sum / tokens;
                double divisor = Math.Sqrt(Math.Max(0, squares / tokens - mean * mean) + 1e-9);
                for (int token = 0; token < tokens; token++)
                {
                    normalized[token * frames + frame] = (float)((attention[token * stride + entry] - mean) / divisor);
                }
            }

            for (int token = 0; token < tokens; token++)
            {
                int row = token * frames;
                for (int frame = 0; frame < frames; frame++)
                {
                    for (int offset = -FilterRadius; offset <= FilterRadius; offset++)
                    {
                        int reflected = Reflect(frame + offset, frames);
                        window[offset + FilterRadius] = normalized[row + reflected];
                    }
                    window.Sort();
                    cost[row + frame] -= window[FilterRadius];
                }
            }
        }

        // Minimum-cost monotonic path with a diagonal, time-only, or token-only advance.
        var moves = new byte[cost.Length];
        for (int token = 0; token < tokens; token++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int frame = 0; frame < frames; frame++)
            {
                if (token == 0 && frame == 0) continue;
                int i = token * frames + frame;
                float diagonal = token > 0 && frame > 0 ? cost[i - frames - 1] : float.PositiveInfinity;
                float left = frame > 0 ? cost[i - 1] : float.PositiveInfinity;
                float above = token > 0 ? cost[i - frames] : float.PositiveInfinity;
                if (diagonal <= left && diagonal <= above)
                {
                    cost[i] += diagonal;
                }
                else if (left <= above)
                {
                    cost[i] += left;
                    moves[i] = 1;
                }
                else
                {
                    cost[i] += above;
                    moves[i] = 2;
                }
            }
        }
        var starts = new int[tokens];
        int rowIndex = tokens - 1;
        int column = frames - 1;
        while (rowIndex != 0 || column != 0)
        {
            starts[rowIndex] = column;
            byte move = moves[rowIndex * frames + column];
            if (move != 1) rowIndex--;
            if (move != 2) column--;
        }
        starts[0] = 0;
        return starts;
    }

    private static int Reflect(int frame, int frames)
    {
        if (frames == 1) return 0;
        int period = 2 * (frames - 1);
        int wrapped = Math.Abs(frame) % period;
        return Math.Min(wrapped, period - wrapped);
    }

    private static WhisperWordTiming TrimSilence(float[] pcm, int pcmOffset, string text, int start, int end)
    {
        int unrefinedStart = start, unrefinedEnd = end;
        int firstActive = end;
        int lastActive = start;
        const int energyWindow = 160;
        for (int sample = start; sample < end; sample += energyWindow)
        {
            int limit = Math.Min(sample + energyWindow, end);
            double energy = 0;
            for (int i = sample; i < limit; i++)
            {
                energy += (double)pcm[pcmOffset + i] * pcm[pcmOffset + i];
            }
            if (energy / (limit - sample) > 1e-5)
            {
                firstActive = Math.Min(firstActive, sample);
                lastActive = limit;
            }
        }
        if (lastActive == start)
        {
            return new(text, start, start, unrefinedStart, unrefinedEnd);
        }
        if (firstActive - start >= 640) start = firstActive;
        if (end - lastActive >= 960) end = lastActive;
        return new(text, start, end, unrefinedStart, unrefinedEnd);
    }
}
