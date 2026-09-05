using System.Numerics;

namespace Aurelian.Audio;

public readonly record struct AudioAssetId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct AudioVoiceId(ulong Value);

public readonly record struct AudioEventId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct AudioBusId(string Value)
{
    public static AudioBusId Master { get; } = new("Master");
    public static AudioBusId Music { get; } = new("Music");
    public static AudioBusId Sfx { get; } = new("Sfx");
    public static AudioBusId Ambient { get; } = new("Ambient");
    public static AudioBusId UI { get; } = new("UI");
    public static AudioBusId Dialogue { get; } = new("Dialogue");

    public override string ToString() => Value;
}

public enum AudioResourceStrategy
{
    Resident,
    Stream
}

public enum AudioVoiceExhaustionPolicy
{
    RejectNewest,
    StealLowestPriorityOldest
}

public enum AudioDiagnosticKind
{
    UnknownAsset,
    UnsupportedFormat,
    DecodeFailure,
    DeviceFailure,
    VoiceCapacity,
    DisposedResource,
    InvalidValue,
    DuplicateEvent
}

public enum AudioCompletionReason
{
    Finished,
    Stopped,
    Stolen
}

public enum AudioFocusPolicy
{
    KeepPlaying,
    MuteGameplay
}

public sealed record AudioClipResource(
    AudioAssetId Id,
    string ContentHash,
    int SampleRateHz,
    int Channels,
    long FrameCount,
    float[] Samples,
    bool LoopByDefault = false,
    AudioResourceStrategy Strategy = AudioResourceStrategy.Resident)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)FrameCount / SampleRateHz);
}

public sealed record AudioCue(
    AudioEventId EventId,
    AudioAssetId Asset,
    AudioBusId Bus,
    float Volume = 1f,
    bool Loop = false,
    int Priority = 0,
    Vector2? Position = null,
    TimeSpan? FadeIn = null);

public readonly record struct AudioListener2D(Vector2 Position, float PanRange, float NearDistance, float MaxDistance);

public sealed record AudioCompletion(
    AudioVoiceId Voice,
    AudioEventId Event,
    AudioAssetId Asset,
    AudioCompletionReason Reason);

public sealed record AudioDiagnostic(AudioDiagnosticKind Kind, string Message);

public sealed record AudioVoiceFact(
    AudioVoiceId Voice,
    AudioEventId Event,
    AudioAssetId Asset,
    AudioBusId Bus,
    TimeSpan Age,
    float EffectiveGain,
    float Pan,
    float Attenuation,
    bool Looping,
    int Priority);

public sealed record AudioRuntimeFacts(
    IReadOnlyList<AudioVoiceFact> ActiveVoices,
    IReadOnlyDictionary<AudioBusId, float> BusGains,
    AudioAssetId? CurrentMusic,
    long SubmittedFrames,
    int DroppedVoices,
    int StolenVoices,
    int DuplicateEvents,
    bool BackendAvailable,
    string BackendName);

public interface IAudioOutputBackend : IDisposable
{
    string Name { get; }
    bool IsAvailable { get; }
    long SubmittedFrames { get; }
    void Submit(ReadOnlySpan<float> interleavedStereoSamples, int sampleRateHz);
}

public interface IAurelianAudioRuntime : IDisposable
{
    void Update(TimeSpan elapsed);
    void SetFocused(bool focused);
}
