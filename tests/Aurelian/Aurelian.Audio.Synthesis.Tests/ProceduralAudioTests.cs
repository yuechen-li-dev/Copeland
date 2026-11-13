using Aurelian.Audio;
using Xunit;

namespace Aurelian.Audio.Synthesis.Tests;

public sealed class ProceduralAudioTests
{
    [Theory]
    [InlineData(ProceduralSoundKind.Footstep)]
    [InlineData(ProceduralSoundKind.MetalImpact)]
    [InlineData(ProceduralSoundKind.Rain)]
    public void SeededRecipesProduceBoundedReplayablePcmThroughTheRealMixer(ProceduralSoundKind kind)
    {
        var recipe = new ProceduralSoundRecipe(kind, Seed: 73);
        var clip = recipe.Prepare(new("sound"));
        var replay = recipe.Prepare(new("another-label"));
        Assert.Equal(clip.ContentHash, replay.ContentHash);
        Assert.Equal(clip.Samples, replay.Samples);
        Assert.NotEqual(clip.ContentHash, (recipe with { Seed = 74 }).Prepare(new("variant")).ContentHash);
        Assert.Equal(12_000, clip.FrameCount);
        Assert.Equal(0, clip.Samples[0]);
        Assert.Equal(0, clip.Samples[^1]);
        Assert.All(clip.Samples, sample => Assert.True(float.IsFinite(sample) && Math.Abs(sample) <= recipe.Gain));
        Assert.True(clip.Samples.Max() - clip.Samples.Min() > .01f);

        using var resources = new AudioResourceScope();
        resources.Add(clip);
        var output = new CapturedOutput();
        using var mixer = new AurelianAudioRuntime(resources, output);
        var cue = new AudioCue(new("step-1"), clip.Id, AudioBusId.Sfx);
        AudioVoiceId voice = mixer.Play(cue)!.Value;
        mixer.Play(cue);
        mixer.Update(TimeSpan.FromSeconds(.3));
        Assert.True(output.SubmittedFrames > 0);
        Assert.Contains(output.Samples, value => Math.Abs(value) > .001f);
        Assert.Equal(1, mixer.Inspect().DuplicateEvents);
        if (kind == ProceduralSoundKind.Rain)
        {
            Assert.Single(mixer.Inspect().ActiveVoices);
            mixer.Stop(voice);
            Assert.Contains(mixer.DrainCompletions(), item => item.Reason == AudioCompletionReason.Stopped);
        }
        else
        {
            Assert.Empty(mixer.Inspect().ActiveVoices);
            Assert.Contains(mixer.DrainCompletions(), item => item.Reason == AudioCompletionReason.Finished);
        }
    }

    [Fact]
    public void PreparationRejectsUnboundedParametersAndCanBeCancelled()
    {
        var recipe = new ProceduralSoundRecipe(ProceduralSoundKind.Rain);
        Assert.Throws<ArgumentOutOfRangeException>(() => (recipe with { DurationSeconds = float.NaN }).Prepare(new("bad")));
        Assert.Throws<ArgumentOutOfRangeException>(() => (recipe with { DurationSeconds = 100 }).Prepare(new("bad")));
        Assert.Throws<ArgumentOutOfRangeException>(() => (recipe with { SampleRateHz = int.MaxValue }).Prepare(new("bad")));
        Assert.Throws<OperationCanceledException>(() => recipe.Prepare(new("cancelled"), new CancellationToken(true)));
    }

    private sealed class CapturedOutput : IAudioOutputBackend
    {
        public string Name => "captured PCM";
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
