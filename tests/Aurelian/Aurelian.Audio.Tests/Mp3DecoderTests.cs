using Aurelian.Audio;
using Xunit;

namespace Aurelian.Audio.Tests;

public sealed class Mp3DecoderTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void DecodesStereoMp3IntoAResidentClipWithTheSourcePitch()
    {
        using var scope = new AudioResourceScope();
        AudioClipResource clip = scope.LoadMp3(new AudioAssetId("tone"), Fixture("tone-440.mp3"), loopByDefault: true);
        Assert.Equal(44100, clip.SampleRateHz);
        Assert.Equal(2, clip.Channels);
        Assert.True(clip.LoopByDefault);
        Assert.InRange(clip.Duration.TotalSeconds, 0.48, 0.56);
        Assert.Same(clip, scope.Get(new AudioAssetId("tone")));

        // Count rising zero crossings of the left channel over a steady 0.2 s window: 440 Hz gives about 88.
        int start = 4410;
        int frames = 8820;
        int crossings = 0;
        for (int frame = start + 1; frame < start + frames; frame++)
        {
            if (clip.Samples[(frame - 1) * 2] < 0 && clip.Samples[frame * 2] >= 0)
            {
                crossings++;
            }
        }
        Assert.InRange(crossings, 86, 90);
        Assert.InRange(clip.Samples.Max(), 0.07f, 0.15f); // the fixture is a quiet sine (ffmpeg default amplitude, upmixed).
    }

    [Fact]
    public void RejectsBytesThatAreNotMp3()
    {
        Assert.ThrowsAny<Exception>(() => Mp3Decoder.Decode(new AudioAssetId("noise"), new byte[4096]));
    }
}
