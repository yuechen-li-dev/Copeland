using System.Diagnostics;

namespace Aurelian.Inference.Whisper;

internal sealed record WhisperDecodeTrace(int FirstSample, int SampleCount, int LastClosedSample,
    int CompletedTextTokens, IReadOnlyList<int> OutputTokens, string Text);

internal sealed class WhisperProfile
{
    public Dictionary<string, double> PhasesMs { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> PipelinesMs { get; } = new(StringComparer.Ordinal);
    public List<WhisperDecodeTrace> Windows { get; } = [];

    public static void Record(WhisperProfile? profile, string phase, long start)
    {
        if (profile is not null)
        {
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            profile.PhasesMs[phase] = profile.PhasesMs.GetValueOrDefault(phase) + elapsed;
        }
    }
}
