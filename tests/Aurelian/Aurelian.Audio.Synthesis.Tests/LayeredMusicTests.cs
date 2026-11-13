using Aurelian.Audio;
using Xunit;

namespace Aurelian.Audio.Synthesis.Tests;

public sealed class LayeredMusicTests
{
    [Fact]
    public void LongGainRampsRemainWithinTheirEndpointsWithoutPhaseInversion()
    {
        using var resources = Resources();
        var output = new CapturedOutput();
        using var mixer = new AurelianAudioRuntime(resources, output, sampleRateHz: 48_000);
        using var music = mixer.PlayMusicLayers(new("score"), [new(new("base")), new(new("beat"))])!;
        music.SetGains([0, 0], TimeSpan.FromSeconds(10));
        mixer.Update(TimeSpan.FromSeconds(10));
        Assert.All(output.Samples, value => Assert.InRange(value, 0, .5f));
        Assert.All(music.Inspect().Layers, layer => Assert.Equal(0, layer.Gain));
    }

    [Fact]
    public void OneCursorKeepsLayersAlignedAcrossLoopsPauseMuteAndRestart()
    {
        using var resources = Resources();
        var output = new CapturedOutput();
        using var mixer = new AurelianAudioRuntime(resources, output, voiceCapacity: 1, sampleRateHz: 8000);
        using var music = mixer.PlayMusicLayers(new("score"), [new(new("base")), new(new("beat"))])!;
        Assert.Single(mixer.Inspect().ActiveVoices);
        mixer.Update(TimeSpan.FromMilliseconds(2));
        Assert.Equal(32, output.Samples.Count);
        for (int frame = 0; frame < 16; frame++)
        {
            Assert.Equal(frame % 8 == 0 ? .25f : 0, output.Samples[frame * 2]);
            Assert.Equal(frame % 8 == 3 ? .5f : 0, output.Samples[frame * 2 + 1]);
        }
        double cursor = music.Inspect().SourceFrame;
        music.SetPaused(true);
        output.Samples.Clear();
        mixer.Update(TimeSpan.FromMilliseconds(1));
        Assert.Equal(cursor, music.Inspect().SourceFrame);
        Assert.All(output.Samples, value => Assert.Equal(0, value));
        music.SetPaused(false);
        mixer.SetBusVolume(AudioBusId.Music, 0);
        output.Samples.Clear();
        mixer.Update(TimeSpan.FromMilliseconds(.5));
        Assert.All(output.Samples, value => Assert.Equal(0, value));
        mixer.SetBusVolume(AudioBusId.Music, 1);
        Assert.True(music.Reset());
        output.Samples.Clear();
        mixer.Update(TimeSpan.FromMilliseconds(1));
        Assert.Equal(.25f, output.Samples[0]);
        Assert.Equal(.5f, output.Samples[7]);
        mixer.Play(new(new("important"), new("base"), AudioBusId.Sfx, Priority: 20));
        Assert.False(music.Inspect().Playing);
        Assert.False(music.Reset());
        Assert.Single(mixer.Inspect().ActiveVoices);
    }

    [Fact]
    public void SampleRampsAreContinuousAndIndependentOfUpdateSubdivision()
    {
        float[] Render(bool split)
        {
            using var resources = Resources();
            var output = new CapturedOutput();
            using var mixer = new AurelianAudioRuntime(resources, output, sampleRateHz: 8000);
            using var music = mixer.PlayMusicLayers(new("score"), [new(new("base"), 0), new(new("beat"), 0)])!;
            music.SetGains([1, .5f], TimeSpan.FromMilliseconds(2));
            if (split)
            {
                for (int frame = 0; frame < 4; frame++)
                {
                    // Re-projecting unchanged targets must not restart their ramps.
                    music.SetGains([1, .5f], TimeSpan.FromMilliseconds(2));
                    mixer.Update(TimeSpan.FromMilliseconds(.5));
                }
            }
            else
            {
                mixer.Update(TimeSpan.FromMilliseconds(2));
            }
            Assert.Equal(1, music.Inspect().Layers[0].Gain);
            Assert.Equal(.5f, music.Inspect().Layers[1].Gain);
            return output.Samples.ToArray();
        }
        Assert.Equal(Render(false), Render(true));
    }

    [Fact]
    public void InvalidGroupsAndGainEditsAreAtomicAndCapacityRejectsTheWholeGroup()
    {
        using var resources = Resources();
        resources.Add(new(new("wrong-period"), "wrong", 8000, 1, 4, new float[4]));
        using var mixer = new AurelianAudioRuntime(resources, new NullAudioOutputBackend(), voiceCapacity: 1,
            exhaustionPolicy: AudioVoiceExhaustionPolicy.RejectNewest);
        Assert.Throws<ArgumentException>(() => mixer.PlayMusicLayers(new("score"),
            [new(new("base")), new(new("wrong-period"))]));
        Assert.Empty(mixer.Inspect().ActiveVoices);
        using var music = mixer.PlayMusicLayers(new("score"), [new(new("base")), new(new("beat"))])!;
        Assert.Throws<ArgumentOutOfRangeException>(() => music.SetGains([.5f, float.NaN], TimeSpan.Zero));
        Assert.All(music.Inspect().Layers, layer => Assert.Equal(1, layer.Target));
        Assert.Null(mixer.PlayMusicLayers(new("another-score"), [new(new("base")), new(new("beat"))]));
        Assert.True(music.Inspect().Playing);
        Assert.Single(mixer.Inspect().ActiveVoices);
    }

    private static AudioResourceScope Resources()
    {
        var resources = new AudioResourceScope();
        float[] left = new float[16];
        left[0] = .25f;
        float[] right = new float[16];
        right[7] = .5f;
        resources.Add(new(new("base"), "base", 8000, 2, 8, left));
        resources.Add(new(new("beat"), "beat", 8000, 2, 8, right));
        return resources;
    }

    private sealed class CapturedOutput : IAudioOutputBackend
    {
        public string Name => "layered PCM";
        public bool IsAvailable => true;
        public long SubmittedFrames { get; private set; }
        public List<float> Samples { get; } = [];
        public void Submit(ReadOnlySpan<float> samples, int sampleRateHz)
        {
            SubmittedFrames += samples.Length / 2;
            Samples.AddRange(samples.ToArray());
        }
        public void Dispose() { }
    }
}
