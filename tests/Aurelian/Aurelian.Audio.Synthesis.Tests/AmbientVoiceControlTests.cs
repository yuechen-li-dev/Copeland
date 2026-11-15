using Aurelian.Audio;
using Xunit;

namespace Aurelian.Audio.Synthesis.Tests;

public sealed class AmbientVoiceControlTests
{
    [Fact]
    public void AmbientLayersRespectEffectsVolumeFocusPauseAndDoNotCreateSceneBuses()
    {
        using var resources = new AudioResourceScope();
        resources.Add(new ProceduralSoundRecipe(ProceduralSoundKind.Wind).Prepare(new("wind")));
        using var mixer = new AurelianAudioRuntime(resources, new NullAudioOutputBackend());
        int buses = mixer.Inspect().BusGains.Count;
        for (int index = 0; index < 8; index++)
        {
            using var ambience = new EnvironmentalAmbience(mixer, "scene-" + index, new("wind"), new("wind"));
            ambience.Update(TimeSpan.FromSeconds(3), 1, 0);
            mixer.SetBusVolume(AudioBusId.Ambient, .5f);
            var outside = mixer.Inspect().ActiveVoices[0];
            Assert.InRange(outside.EffectiveGain, .49f, .5f);
            mixer.SetFocused(false);
            Assert.All(mixer.Inspect().ActiveVoices, voice => Assert.Equal(0, voice.EffectiveGain));
            mixer.SetFocused(true);
            mixer.SetBusPaused(AudioBusId.Ambient, true);
            var ages = mixer.Inspect().ActiveVoices.Select(voice => voice.Age).ToArray();
            mixer.Update(TimeSpan.FromSeconds(.1));
            Assert.Equal(ages, mixer.Inspect().ActiveVoices.Select(voice => voice.Age));
            mixer.SetBusPaused(AudioBusId.Ambient, false);
        }
        Assert.Empty(mixer.Inspect().ActiveVoices);
        Assert.Equal(buses, mixer.Inspect().BusGains.Count);
    }
}
