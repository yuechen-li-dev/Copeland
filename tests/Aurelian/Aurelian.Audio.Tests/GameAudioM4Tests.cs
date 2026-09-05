using System.Numerics;
using Aurelian.Audio.NAudio;
using Dominatus.Actuators.Audio;
using Dominatus.Audio.Aurelian;
using Xunit;

namespace Aurelian.Audio.Tests;

public sealed class GameAudioM4Tests
{
    [Fact]
    public void AssetIdentityAndResourceMetadata_AreTypedAndValidated()
    {
        using var resources = new AudioResourceScope();
        AudioClipResource clip = resources.Add(Clip("sword", frames: 48));

        Assert.Equal(new AudioAssetId("sword"), clip.Id);
        Assert.Equal(48, clip.FrameCount);
        Assert.Equal(TimeSpan.FromMilliseconds(1), clip.Duration);
        Assert.Throws<ArgumentException>(() => resources.Add(Clip("", frames: 1)));
        Assert.Throws<NotSupportedException>(() => resources.Add(
            Clip("stream", frames: 1) with { Strategy = AudioResourceStrategy.Stream }));
    }

    [Fact]
    public void PcmWavLoad_DecodesAuthoredMono16AndRejectsUnsupportedData()
    {
        string path = TemporaryWav("authored", sampleRate: 8_000, frames: 16);
        using var resources = new AudioResourceScope();
        AudioClipResource clip = resources.LoadPcmWav(new AudioAssetId("authored"), path);

        Assert.Equal(8_000, clip.SampleRateHz);
        Assert.Equal(1, clip.Channels);
        Assert.Equal(16, clip.FrameCount);
        Assert.NotEqual(string.Empty, clip.ContentHash);
        Assert.Throws<InvalidDataException>(() => PcmWavDecoder.Decode(new AudioAssetId("bad"), "bad"u8));
    }

    [Fact]
    public void OneShotCompletesAndLoopStopsExplicitly()
    {
        var backend = new NullAudioOutputBackend();
        using var runtime = Runtime(backend, Clip("one", frames: 4), Clip("loop", frames: 2, loop: true));
        AudioVoiceId one = runtime.Play(Cue("event-one", "one"))!.Value;
        AudioVoiceId loop = runtime.Play(Cue("event-loop", "loop", loop: true))!.Value;

        runtime.Update(TimeSpan.FromMilliseconds(1));

        AudioCompletion completion = Assert.Single(runtime.DrainCompletions(), item => item.Voice == one);
        Assert.Equal(AudioCompletionReason.Finished, completion.Reason);
        Assert.Contains(runtime.Inspect().ActiveVoices, item => item.Voice == loop);
        Assert.True(runtime.Stop(loop));
        Assert.Contains(runtime.DrainCompletions(), item => item.Reason == AudioCompletionReason.Stopped);
    }

    [Fact]
    public void VoiceCapacity_StealsLowestPriorityOldestDeterministically()
    {
        using var resources = Resources(Clip("tone", frames: 48_000, loop: true));
        var backend = new NullAudioOutputBackend();
        using var runtime = new AurelianAudioRuntime(resources, backend, voiceCapacity: 2);
        AudioVoiceId oldest = runtime.Play(Cue("one", "tone", priority: 0, loop: true))!.Value;
        runtime.Play(Cue("two", "tone", priority: 1, loop: true));
        AudioVoiceId incoming = runtime.Play(Cue("three", "tone", priority: 2, loop: true))!.Value;

        AudioCompletion stolen = Assert.Single(runtime.DrainCompletions());
        Assert.Equal(oldest, stolen.Voice);
        Assert.Equal(AudioCompletionReason.Stolen, stolen.Reason);
        Assert.Contains(runtime.Inspect().ActiveVoices, item => item.Voice == incoming);
        Assert.Equal(1, runtime.Inspect().StolenVoices);
    }

    [Fact]
    public void RejectNewestPolicy_DropsWithoutBackendDependentChoice()
    {
        using var resources = Resources(Clip("tone", frames: 48_000, loop: true));
        using var runtime = new AurelianAudioRuntime(
            resources,
            new NullAudioOutputBackend(),
            voiceCapacity: 1,
            exhaustionPolicy: AudioVoiceExhaustionPolicy.RejectNewest);
        Assert.NotNull(runtime.Play(Cue("one", "tone", loop: true)));
        Assert.Null(runtime.Play(Cue("two", "tone", loop: true)));
        Assert.Equal(1, runtime.Inspect().DroppedVoices);
    }

    [Fact]
    public void BusMasterAndMute_UseCanonicalMultiplicationWithoutCorruptingVoices()
    {
        using var runtime = Runtime(new NullAudioOutputBackend(), Clip("music", frames: 48_000, loop: true));
        runtime.SetBusVolume(AudioBusId.Music, 0.5f);
        runtime.SetBusVolume(AudioBusId.Master, 0.8f);
        runtime.Play(Cue("music", "music", bus: AudioBusId.Music, volume: 0.5f, loop: true));

        Assert.Equal(0.2f, Assert.Single(runtime.Inspect().ActiveVoices).EffectiveGain, 4);
        runtime.SetBusMuted(AudioBusId.Sfx, true);
        Assert.Equal(0.2f, Assert.Single(runtime.Inspect().ActiveVoices).EffectiveGain, 4);
        runtime.SetBusMuted(AudioBusId.Music, true);
        Assert.Equal(0f, Assert.Single(runtime.Inspect().ActiveVoices).EffectiveGain);
        Assert.Single(runtime.Inspect().ActiveVoices);
    }

    [Fact]
    public void FadeAndCrossfade_AreDrivenByExplicitElapsedTime()
    {
        using var runtime = Runtime(
            new NullAudioOutputBackend(),
            Clip("a", frames: 96_000, loop: true),
            Clip("b", frames: 96_000, loop: true));
        runtime.SetMusic(new AudioEventId("music-a"), new AudioAssetId("a"), TimeSpan.Zero);
        runtime.CrossfadeMusic(new AudioEventId("music-b"), new AudioAssetId("b"), TimeSpan.FromSeconds(1));

        runtime.Update(TimeSpan.FromMilliseconds(500));

        AudioVoiceFact[] voices = runtime.Inspect().ActiveVoices.OrderBy(item => item.Asset.Value).ToArray();
        Assert.Equal(2, voices.Length);
        Assert.InRange(voices[0].EffectiveGain, 0.49f, 0.51f);
        Assert.InRange(voices[1].EffectiveGain, 0.49f, 0.51f);
        runtime.Update(TimeSpan.FromMilliseconds(500));
        Assert.Single(runtime.Inspect().ActiveVoices);
        Assert.Equal(new AudioAssetId("b"), runtime.Inspect().CurrentMusic);
    }

    [Fact]
    public void SpatialPanAndAttenuation_AreBoundedAndDeterministic()
    {
        var backend = new NullAudioOutputBackend();
        using var runtime = Runtime(backend, Clip("tone", frames: 48_000, sample: 0.5f, loop: true));
        runtime.Listener = new AudioListener2D(Vector2.Zero, PanRange: 10f, NearDistance: 2f, MaxDistance: 12f);
        runtime.Play(Cue("right", "tone", loop: true, position: new Vector2(5f, 0f)));

        AudioVoiceFact fact = Assert.Single(runtime.Inspect().ActiveVoices);
        Assert.Equal(0.5f, fact.Pan, 4);
        Assert.Equal(0.7f, fact.Attenuation, 4);
        runtime.Update(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 48_000));
        Assert.True(backend.LastSubmission[1] > backend.LastSubmission[0]);
    }

    [Fact]
    public void EventDeduplication_RealizesSameEventOnceAndDistinctEventsTwice()
    {
        using var runtime = Runtime(new NullAudioOutputBackend(), Clip("tone", frames: 48_000));
        Assert.NotNull(runtime.Play(Cue("same", "tone")));
        Assert.Null(runtime.Play(Cue("same", "tone")));
        Assert.NotNull(runtime.Play(Cue("different", "tone")));
        Assert.Equal(2, runtime.Inspect().ActiveVoices.Count);
        Assert.Equal(1, runtime.Inspect().DuplicateEvents);
    }

    [Fact]
    public void PauseAndFocusPolicy_AffectProjectionOnly()
    {
        using var runtime = Runtime(new NullAudioOutputBackend(), Clip("tone", frames: 48_000, loop: true));
        runtime.Play(Cue("sfx", "tone", loop: true));
        runtime.Play(Cue("music", "tone", bus: AudioBusId.Music, loop: true));
        runtime.SetFocused(false);
        runtime.Update(TimeSpan.FromMilliseconds(10));

        AudioVoiceFact sfx = runtime.Inspect().ActiveVoices.Single(item => item.Bus == AudioBusId.Sfx);
        AudioVoiceFact music = runtime.Inspect().ActiveVoices.Single(item => item.Bus == AudioBusId.Music);
        Assert.Equal(0f, sfx.EffectiveGain);
        Assert.Equal(1f, music.EffectiveGain);
        runtime.SetBusPaused(AudioBusId.Music, true);
        TimeSpan age = music.Age;
        runtime.Update(TimeSpan.FromMilliseconds(10));
        Assert.Equal(age, runtime.Inspect().ActiveVoices.Single(item => item.Bus == AudioBusId.Music).Age);
    }

    [Fact]
    public void UnknownAssetInvalidGainAndDisposal_AreExplicit()
    {
        var resources = Resources(Clip("tone", frames: 4));
        var runtime = new AurelianAudioRuntime(resources, new NullAudioOutputBackend());
        Assert.Null(runtime.Play(Cue("unknown", "missing")));
        Assert.Equal(AudioDiagnosticKind.UnknownAsset, Assert.Single(runtime.DrainDiagnostics()).Kind);
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.SetBusVolume(AudioBusId.Sfx, 1.1f));
        runtime.Dispose();
        Assert.Throws<ObjectDisposedException>(() => runtime.Update(TimeSpan.Zero));
        Assert.Throws<ObjectDisposedException>(() => resources.Get(new AudioAssetId("tone")));
    }

    [Fact]
    public void DeviceLossBecomesDiagnosticAndDoesNotEscapeIntoGameplay()
    {
        using var runtime = new AurelianAudioRuntime(Resources(Clip("tone", frames: 48)), new FailingBackend());
        runtime.Play(Cue("device-loss", "tone"));

        runtime.Update(TimeSpan.FromMilliseconds(1));

        AudioDiagnostic diagnostic = Assert.Single(runtime.DrainDiagnostics());
        Assert.Equal(AudioDiagnosticKind.DeviceFailure, diagnostic.Kind);
    }

    [Fact]
    public void ThousandOneShots_ReleaseWithStableVoiceCount()
    {
        using var runtime = Runtime(new NullAudioOutputBackend(), Clip("tick", frames: 1));
        for (int index = 0; index < 1_000; index++)
        {
            Assert.NotNull(runtime.Play(Cue($"tick-{index}", "tick")));
            runtime.Update(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 48_000));
        }
        Assert.Empty(runtime.Inspect().ActiveVoices);
        Assert.Equal(1_000, runtime.DrainCompletions().Count);
    }

    [Fact]
    public async Task DominatusGeneratedArtifact_LoadsWithoutGenerationPlaybackConflation()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dominatus-audio-{Guid.NewGuid():N}.wav");
        var provider = new FakeAudioProvider(sampleRateHz: 22_050);
        GenerateSoundEffectResult generated = await provider.GenerateSoundEffectAsync(
            new GenerateSoundEffectCommand
            {
                ProviderId = "fake",
                IdempotencyKey = "aurelian-m4-generated",
                Prompt = "harvest pop",
                OutputPath = path,
                DurationSeconds = 0.02
            },
            default);
        using var resources = new AudioResourceScope();
        AudioClipResource resource = DominatusAudioArtifactAdapter.Load(
            resources,
            new AudioAssetId("generated.harvest"),
            generated.Artifact);
        AudioEventId correlation = DominatusAudioArtifactAdapter.CorrelationId(generated.Metadata, resource.Id);

        Assert.Equal(22_050, resource.SampleRateHz);
        Assert.StartsWith("dominatus:", correlation.Value);
        using var runtime = new AurelianAudioRuntime(resources, new NullAudioOutputBackend());
        Assert.NotNull(runtime.Play(new AudioCue(correlation, resource.Id, AudioBusId.Dialogue)));
    }

    [Fact]
    public void WindowsBackend_OpensAndSubmitsWhenDeviceIsAvailable()
    {
        if (!NAudioOutputBackend.TryCreate(out NAudioOutputBackend? backend, out string? error))
        {
            Assert.False(string.IsNullOrWhiteSpace(error));
            return;
        }
        using (NAudioOutputBackend availableBackend = backend!)
        {
            availableBackend.Submit(new float[960], 48_000);
            Assert.True(availableBackend.IsAvailable);
            Assert.Equal(480, availableBackend.SubmittedFrames);
        }
    }

    private static AurelianAudioRuntime Runtime(NullAudioOutputBackend backend, params AudioClipResource[] clips)
    {
        return new AurelianAudioRuntime(Resources(clips), backend);
    }

    private static AudioResourceScope Resources(params AudioClipResource[] clips)
    {
        var resources = new AudioResourceScope();
        foreach (AudioClipResource clip in clips)
        {
            resources.Add(clip);
        }
        return resources;
    }

    private static AudioClipResource Clip(string id, int frames, float sample = 0.25f, bool loop = false)
    {
        return new AudioClipResource(new AudioAssetId(id), $"hash-{id}", 48_000, 1, frames, Enumerable.Repeat(sample, frames).ToArray(), loop);
    }

    private static AudioCue Cue(
        string eventId,
        string assetId,
        AudioBusId? bus = null,
        float volume = 1f,
        bool loop = false,
        int priority = 0,
        Vector2? position = null)
    {
        return new AudioCue(new AudioEventId(eventId), new AudioAssetId(assetId), bus ?? AudioBusId.Sfx, volume, loop, priority, position);
    }

    private static string TemporaryWav(string name, int sampleRate, int frames)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid():N}.wav");
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        int dataBytes = frames * sizeof(short);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        for (int index = 0; index < frames; index++)
        {
            writer.Write((short)(index % 2 == 0 ? 4_000 : -4_000));
        }
        return path;
    }

    private sealed class FailingBackend : IAudioOutputBackend
    {
        public string Name => "Failing";
        public bool IsAvailable => false;
        public long SubmittedFrames => 0;
        public void Submit(ReadOnlySpan<float> interleavedStereoSamples, int sampleRateHz) =>
            throw new IOException("device unavailable");
        public void Dispose()
        {
        }
    }
}
