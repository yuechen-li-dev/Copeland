namespace Aurelian.Audio;

public sealed record MusicLayer(AudioAssetId Asset, float Gain = 1);
public sealed record MusicLayerGain(AudioAssetId Asset, float Gain, float Target);
public sealed record LayeredMusicFacts(bool Playing, double SourceFrame, IReadOnlyList<MusicLayerGain> Layers);

/// <summary>
/// One logical Music voice with a shared sample cursor and independently ramped layers.
/// Capacity admission, stealing, pause, stop and restart apply to the entire group.
/// </summary>
public sealed class LayeredMusic : IDisposable
{
    private readonly AurelianAudioRuntime runtime;
    private bool disposed;

    internal LayeredMusic(AurelianAudioRuntime runtime, AudioVoiceId voice)
    {
        this.runtime = runtime;
        Voice = voice;
    }

    public AudioVoiceId Voice { get; }
    public LayeredMusicFacts Inspect() => runtime.InspectMusicLayers(Voice);

    public bool SetGains(IReadOnlyList<float> gains, TimeSpan transition)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return runtime.SetMusicLayerGains(Voice, gains, transition);
    }

    public bool SetPaused(bool paused)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return runtime.SetVoicePaused(Voice, paused);
    }

    /// <summary>Return all layers to frame zero and their authored initial gains, without reallocating a voice.</summary>
    public bool Reset()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return runtime.ResetMusicLayers(Voice);
    }

    public void Dispose()
    {
        if (!disposed)
        {
            runtime.Stop(Voice);
            disposed = true;
        }
    }
}
