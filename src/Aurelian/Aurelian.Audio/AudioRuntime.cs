using System.Numerics;

namespace Aurelian.Audio;

public sealed class AurelianAudioRuntime : IAurelianAudioRuntime
{
    private readonly AudioResourceScope resources;
    private readonly IAudioOutputBackend backend;
    private readonly AudioVoiceExhaustionPolicy exhaustionPolicy;
    private readonly AudioFocusPolicy focusPolicy;
    private readonly int sampleRateHz;
    private readonly int voiceCapacity;
    private readonly int dedupeCapacity;
    private readonly List<Voice> voices = [];
    private readonly Dictionary<AudioBusId, BusState> buses = [];
    private readonly HashSet<AudioEventId> consumedEvents = [];
    private readonly Queue<AudioEventId> consumedEventOrder = [];
    private readonly Queue<AudioCompletion> completions = [];
    private readonly Queue<AudioDiagnostic> diagnostics = [];
    private bool disposed;
    private bool focused = true;
    private ulong nextVoiceId;
    private ulong allocationSequence;
    private int droppedVoices;
    private int stolenVoices;
    private int duplicateEvents;

    public AurelianAudioRuntime(
        AudioResourceScope resources,
        IAudioOutputBackend backend,
        int voiceCapacity = 32,
        int sampleRateHz = 48_000,
        AudioVoiceExhaustionPolicy exhaustionPolicy = AudioVoiceExhaustionPolicy.StealLowestPriorityOldest,
        AudioFocusPolicy focusPolicy = AudioFocusPolicy.MuteGameplay,
        int dedupeCapacity = 4096)
    {
        this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        if (voiceCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(voiceCapacity));
        }
        if (sampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }
        if (dedupeCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dedupeCapacity));
        }
        this.voiceCapacity = voiceCapacity;
        this.sampleRateHz = sampleRateHz;
        this.exhaustionPolicy = exhaustionPolicy;
        this.focusPolicy = focusPolicy;
        this.dedupeCapacity = dedupeCapacity;
        foreach (AudioBusId bus in new[] { AudioBusId.Master, AudioBusId.Music, AudioBusId.Sfx, AudioBusId.Ambient, AudioBusId.UI, AudioBusId.Dialogue })
        {
            buses.Add(bus, new BusState());
        }
    }

    public AudioListener2D Listener { get; set; } = new(Vector2.Zero, 10f, 1f, 20f);

    public AudioVoiceId? Play(AudioCue cue)
    {
        ThrowIfDisposed();
        ValidateCue(cue);
        if (!TryConsume(cue.EventId))
        {
            duplicateEvents++;
            diagnostics.Enqueue(new AudioDiagnostic(AudioDiagnosticKind.DuplicateEvent, $"Duplicate audio event '{cue.EventId}' was ignored."));
            return null;
        }
        if (!resources.TryGet(cue.Asset, out AudioClipResource? resource))
        {
            diagnostics.Enqueue(new AudioDiagnostic(AudioDiagnosticKind.UnknownAsset, $"Unknown audio asset '{cue.Asset}'."));
            return null;
        }
        if (voices.Count >= voiceCapacity && !MakeRoom(cue.Priority))
        {
            droppedVoices++;
            diagnostics.Enqueue(new AudioDiagnostic(AudioDiagnosticKind.VoiceCapacity, $"Voice capacity {voiceCapacity} rejected event '{cue.EventId}'."));
            return null;
        }

        AudioVoiceId id = new(++nextVoiceId);
        voices.Add(new Voice(id, cue, resource!, ++allocationSequence));
        return id;
    }

    public AudioVoiceId? SetMusic(AudioEventId eventId, AudioAssetId asset, TimeSpan fadeIn)
    {
        StopBus(AudioBusId.Music, TimeSpan.Zero);
        return Play(new AudioCue(eventId, asset, AudioBusId.Music, Loop: true, FadeIn: fadeIn));
    }

    public AudioVoiceId? CrossfadeMusic(AudioEventId eventId, AudioAssetId asset, TimeSpan duration)
    {
        ValidateDuration(duration);
        StopBus(AudioBusId.Music, duration);
        return Play(new AudioCue(eventId, asset, AudioBusId.Music, Loop: true, FadeIn: duration));
    }

    public bool Stop(AudioVoiceId id, TimeSpan fadeOut = default)
    {
        ThrowIfDisposed();
        ValidateDuration(fadeOut);
        Voice? voice = voices.SingleOrDefault(item => item.Id == id);
        if (voice is null)
        {
            return false;
        }
        if (fadeOut == TimeSpan.Zero)
        {
            Complete(voice, AudioCompletionReason.Stopped);
        }
        else
        {
            voice.BeginFade(voice.FadeGain, 0f, fadeOut, stopAtEnd: true);
        }
        return true;
    }

    public void StopBus(AudioBusId bus, TimeSpan fadeOut)
    {
        ThrowIfDisposed();
        ValidateDuration(fadeOut);
        foreach (Voice voice in voices.Where(item => item.Cue.Bus == bus).ToArray())
        {
            Stop(voice.Id, fadeOut);
        }
    }

    public void SetBusVolume(AudioBusId bus, float volume)
    {
        ThrowIfDisposed();
        ValidateUnit(volume, nameof(volume));
        GetBus(bus).Volume = volume;
    }

    public void SetBusMuted(AudioBusId bus, bool muted)
    {
        ThrowIfDisposed();
        GetBus(bus).Muted = muted;
    }

    public void SetBusPaused(AudioBusId bus, bool paused)
    {
        ThrowIfDisposed();
        GetBus(bus).Paused = paused;
    }

    public void SetFocused(bool focused)
    {
        ThrowIfDisposed();
        this.focused = focused;
    }

    public void Update(TimeSpan elapsed)
    {
        ThrowIfDisposed();
        ValidateDuration(elapsed);
        int frames = checked((int)Math.Round(elapsed.TotalSeconds * sampleRateHz, MidpointRounding.AwayFromZero));
        if (frames == 0)
        {
            return;
        }
        var mixed = new float[frames * 2];
        foreach (Voice voice in voices.ToArray())
        {
            MixVoice(voice, mixed, frames);
        }
        for (int index = 0; index < mixed.Length; index++)
        {
            mixed[index] = Math.Clamp(mixed[index], -1f, 1f);
        }
        try
        {
            backend.Submit(mixed, sampleRateHz);
        }
        catch (Exception exception)
        {
            diagnostics.Enqueue(new AudioDiagnostic(AudioDiagnosticKind.DeviceFailure, $"Audio backend '{backend.Name}' failed: {exception.Message}"));
        }
    }

    public IReadOnlyList<AudioCompletion> DrainCompletions()
    {
        ThrowIfDisposed();
        return Drain(completions);
    }

    public IReadOnlyList<AudioDiagnostic> DrainDiagnostics()
    {
        ThrowIfDisposed();
        return Drain(diagnostics);
    }

    public AudioRuntimeFacts Inspect()
    {
        ThrowIfDisposed();
        var facts = voices.Select(voice =>
        {
            (float pan, float attenuation) = Spatial(voice.Cue.Position);
            return new AudioVoiceFact(
                voice.Id,
                voice.Cue.EventId,
                voice.Cue.Asset,
                voice.Cue.Bus,
                voice.Age,
                EffectiveGain(voice, attenuation),
                pan,
                attenuation,
                voice.Looping,
                voice.Cue.Priority);
        }).ToArray();
        Dictionary<AudioBusId, float> gains = buses.ToDictionary(pair => pair.Key, pair => pair.Value.Muted ? 0f : pair.Value.Volume);
        AudioAssetId? music = voices.LastOrDefault(voice => voice.Cue.Bus == AudioBusId.Music)?.Cue.Asset;
        return new AudioRuntimeFacts(facts, gains, music, backend.SubmittedFrames, droppedVoices, stolenVoices, duplicateEvents, backend.IsAvailable, backend.Name);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        voices.Clear();
        backend.Dispose();
        resources.Dispose();
        disposed = true;
    }

    private void MixVoice(Voice voice, Span<float> output, int outputFrames)
    {
        BusState bus = GetBus(voice.Cue.Bus);
        if (bus.Paused)
        {
            return;
        }
        for (int outputFrame = 0; outputFrame < outputFrames; outputFrame++)
        {
            if (voice.SourceFrame >= voice.Resource.FrameCount)
            {
                if (voice.Looping)
                {
                    voice.SourceFrame %= voice.Resource.FrameCount;
                }
                else
                {
                    Complete(voice, AudioCompletionReason.Finished);
                    return;
                }
            }

            voice.AdvanceFade(1d / sampleRateHz);
            if (voice.StopAtFadeEnd && voice.FadeComplete)
            {
                Complete(voice, AudioCompletionReason.Stopped);
                return;
            }
            (float pan, float attenuation) = Spatial(voice.Cue.Position);
            float gain = EffectiveGain(voice, attenuation);
            float leftPan = pan <= 0f ? 1f : 1f - pan;
            float rightPan = pan >= 0f ? 1f : 1f + pan;
            int sourceFrame = (int)voice.SourceFrame;
            int sourceIndex = sourceFrame * voice.Resource.Channels;
            float left = voice.Resource.Samples[sourceIndex];
            float right = voice.Resource.Channels == 2 ? voice.Resource.Samples[sourceIndex + 1] : left;
            output[outputFrame * 2] += left * gain * leftPan;
            output[outputFrame * 2 + 1] += right * gain * rightPan;
            voice.SourceFrame += (double)voice.Resource.SampleRateHz / sampleRateHz;
            voice.Age += TimeSpan.FromSeconds(1d / sampleRateHz);
            if (!voice.Looping && voice.SourceFrame >= voice.Resource.FrameCount)
            {
                Complete(voice, AudioCompletionReason.Finished);
                return;
            }
        }
    }

    private float EffectiveGain(Voice voice, float attenuation)
    {
        BusState bus = GetBus(voice.Cue.Bus);
        BusState master = GetBus(AudioBusId.Master);
        if (bus.Muted || master.Muted)
        {
            return 0f;
        }
        if (!focused && focusPolicy == AudioFocusPolicy.MuteGameplay && voice.Cue.Bus is { Value: "Sfx" or "Ambient" })
        {
            return 0f;
        }
        return voice.Cue.Volume * voice.FadeGain * bus.Volume * master.Volume * attenuation;
    }

    private (float Pan, float Attenuation) Spatial(Vector2? position)
    {
        if (position is null)
        {
            return (0f, 1f);
        }
        AudioListener2D listener = Listener;
        if (listener.PanRange <= 0f || listener.NearDistance < 0f || listener.MaxDistance <= listener.NearDistance)
        {
            throw new InvalidOperationException("Audio listener ranges are invalid.");
        }
        Vector2 relative = position.Value - listener.Position;
        float pan = Math.Clamp(relative.X / listener.PanRange, -1f, 1f);
        float distance = relative.Length();
        float attenuation = distance <= listener.NearDistance
            ? 1f
            : Math.Clamp(1f - ((distance - listener.NearDistance) / (listener.MaxDistance - listener.NearDistance)), 0f, 1f);
        return (pan, attenuation);
    }

    private bool MakeRoom(int incomingPriority)
    {
        if (exhaustionPolicy == AudioVoiceExhaustionPolicy.RejectNewest)
        {
            return false;
        }
        Voice victim = voices.OrderBy(voice => voice.Cue.Priority).ThenBy(voice => voice.AllocationSequence).First();
        if (incomingPriority < victim.Cue.Priority)
        {
            return false;
        }
        Complete(victim, AudioCompletionReason.Stolen);
        stolenVoices++;
        return true;
    }

    private void Complete(Voice voice, AudioCompletionReason reason)
    {
        if (!voices.Remove(voice))
        {
            return;
        }
        completions.Enqueue(new AudioCompletion(voice.Id, voice.Cue.EventId, voice.Cue.Asset, reason));
    }

    private bool TryConsume(AudioEventId eventId)
    {
        if (!consumedEvents.Add(eventId))
        {
            return false;
        }
        consumedEventOrder.Enqueue(eventId);
        while (consumedEventOrder.Count > dedupeCapacity)
        {
            consumedEvents.Remove(consumedEventOrder.Dequeue());
        }
        return true;
    }

    private BusState GetBus(AudioBusId id)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("Audio bus identity must not be empty.", nameof(id));
        }
        if (!buses.TryGetValue(id, out BusState? bus))
        {
            bus = new BusState();
            buses.Add(id, bus);
        }
        return bus;
    }

    private static T[] Drain<T>(Queue<T> queue)
    {
        var result = new T[queue.Count];
        for (int index = 0; index < result.Length; index++)
        {
            result[index] = queue.Dequeue();
        }
        return result;
    }

    private static void ValidateCue(AudioCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        if (string.IsNullOrWhiteSpace(cue.EventId.Value) || string.IsNullOrWhiteSpace(cue.Asset.Value) || string.IsNullOrWhiteSpace(cue.Bus.Value))
        {
            throw new ArgumentException("Audio event, asset, and bus identities must not be empty.", nameof(cue));
        }
        ValidateUnit(cue.Volume, nameof(cue.Volume));
        if (cue.FadeIn is TimeSpan fade)
        {
            ValidateDuration(fade);
        }
    }

    private static void ValidateUnit(float value, string name)
    {
        if (!float.IsFinite(value) || value is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(name, "Audio gain must be finite and in [0, 1].");
        }
    }

    private static void ValidateDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private sealed class BusState
    {
        public float Volume { get; set; } = 1f;
        public bool Muted { get; set; }
        public bool Paused { get; set; }
    }

    private sealed class Voice
    {
        private double fadeElapsedSeconds;
        private double fadeDurationSeconds;
        private float fadeStart = 1f;
        private float fadeEnd = 1f;

        public Voice(AudioVoiceId id, AudioCue cue, AudioClipResource resource, ulong allocationSequence)
        {
            Id = id;
            Cue = cue;
            Resource = resource;
            AllocationSequence = allocationSequence;
            if (cue.FadeIn is TimeSpan fade && fade > TimeSpan.Zero)
            {
                BeginFade(0f, 1f, fade, stopAtEnd: false);
            }
        }

        public AudioVoiceId Id { get; }
        public AudioCue Cue { get; }
        public AudioClipResource Resource { get; }
        public ulong AllocationSequence { get; }
        public double SourceFrame { get; set; }
        public TimeSpan Age { get; set; }
        public bool Looping => Cue.Loop || Resource.LoopByDefault;
        public float FadeGain { get; private set; } = 1f;
        public bool StopAtFadeEnd { get; private set; }
        public bool FadeComplete => fadeDurationSeconds > 0d && fadeElapsedSeconds + 1e-9 >= fadeDurationSeconds;

        public void BeginFade(float start, float end, TimeSpan duration, bool stopAtEnd)
        {
            fadeStart = start;
            fadeEnd = end;
            fadeElapsedSeconds = 0d;
            fadeDurationSeconds = duration.TotalSeconds;
            FadeGain = duration == TimeSpan.Zero ? end : start;
            StopAtFadeEnd = stopAtEnd;
        }

        public void AdvanceFade(double elapsedSeconds)
        {
            if (fadeDurationSeconds == 0d || FadeComplete)
            {
                return;
            }
            fadeElapsedSeconds += elapsedSeconds;
            float progress = Math.Clamp((float)(fadeElapsedSeconds / fadeDurationSeconds), 0f, 1f);
            FadeGain = fadeStart + ((fadeEnd - fadeStart) * progress);
        }
    }
}

public sealed class NullAudioOutputBackend : IAudioOutputBackend
{
    private bool disposed;

    public string Name => "Null";
    public bool IsAvailable => !disposed;
    public long SubmittedFrames { get; private set; }
    public float[] LastSubmission { get; private set; } = [];

    public void Submit(ReadOnlySpan<float> interleavedStereoSamples, int sampleRateHz)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (sampleRateHz <= 0 || interleavedStereoSamples.Length % 2 != 0)
        {
            throw new ArgumentException("Backend submissions must be stereo frames at a positive sample rate.");
        }
        LastSubmission = interleavedStereoSamples.ToArray();
        SubmittedFrames += interleavedStereoSamples.Length / 2;
    }

    public void Dispose()
    {
        disposed = true;
        LastSubmission = [];
    }
}
