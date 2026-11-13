namespace Aurelian.Audio;

public readonly record struct EnvironmentalAmbienceState(float Rain, float Shelter, float ExteriorGain, float InteriorGain);

/// <summary>Two owned ambient voices on explicit buses, driven by presentation inputs and elapsed time.</summary>
public sealed class EnvironmentalAmbience : IDisposable
{
    private readonly AurelianAudioRuntime runtime;
    private readonly AudioBusId exteriorBus;
    private readonly AudioBusId interiorBus;
    private readonly AudioVoiceId exterior;
    private readonly AudioVoiceId interior;
    private readonly float transitionSeconds;
    private bool disposed;
    public EnvironmentalAmbienceState State { get; private set; }

    public EnvironmentalAmbience(AurelianAudioRuntime runtime, string identity, AudioAssetId outside, AudioAssetId inside,
        float transitionSeconds = .4f)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (string.IsNullOrWhiteSpace(identity) || !float.IsFinite(transitionSeconds) || transitionSeconds <= 0 || transitionSeconds > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(identity));
        }
        this.runtime = runtime;
        this.transitionSeconds = transitionSeconds;
        exteriorBus = new(identity + ":outside");
        interiorBus = new(identity + ":inside");
        runtime.SetBusVolume(exteriorBus, 0);
        runtime.SetBusVolume(interiorBus, 0);
        exterior = runtime.Play(new(new(identity + ":outside"), outside, exteriorBus, Loop: true))
            ?? throw new InvalidOperationException("Exterior ambience voice could not start.");
        AudioVoiceId? second = runtime.Play(new(new(identity + ":inside"), inside, interiorBus, Loop: true));
        if (second is null)
        {
            runtime.Stop(exterior);
            throw new InvalidOperationException("Interior ambience voice could not start.");
        }
        interior = second.Value;
    }

    public void Update(TimeSpan elapsed, float rain, float shelter, bool paused = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromSeconds(10) || !float.IsFinite(rain) || rain < 0 || rain > 1
            || !float.IsFinite(shelter) || shelter < 0 || shelter > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }
        runtime.SetBusPaused(exteriorBus, paused);
        runtime.SetBusPaused(interiorBus, paused);
        float blend = 0;
        if (!paused)
        {
            blend = 1 - MathF.Exp(-(float)elapsed.TotalSeconds / transitionSeconds);
        }
        float nextRain = State.Rain + (rain - State.Rain) * blend;
        float nextShelter = State.Shelter + (shelter - State.Shelter) * blend;
        float outside = nextRain * MathF.Sqrt(Math.Max(0, 1 - nextShelter));
        float inside = nextRain * .35f * MathF.Sqrt(Math.Max(0, nextShelter));
        State = new(nextRain, nextShelter, outside, inside);
        runtime.SetBusVolume(exteriorBus, outside);
        runtime.SetBusVolume(interiorBus, inside);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        runtime.Stop(exterior);
        runtime.Stop(interior);
        disposed = true;
    }
}
