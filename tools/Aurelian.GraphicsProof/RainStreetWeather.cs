using System.Numerics;
using Aurelian.Audio;
using Aurelian.Audio.NAudio;
using Aurelian.Rendering.Contracts.Models;

internal static class RainStreetWeather
{
    public static readonly RainShelter3D Shelter = new(new(-4.6f, -.5f, 8), new(-1.6f, 3.2f, 13), .5f);
    public static readonly WetGroundField3D Ground = CreateGround();

    private static WetGroundField3D CreateGround()
    {
        const int columns = 97;
        const int rows = 257;
        const float water = .025f;
        Vector2 origin = new(-6.5f, -47.5f);
        Vector2 size = new(13, 65);
        var samples = new List<WetGroundSample3D>(columns * rows);
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                Vector2 p = origin + size * new Vector2((float)column / (columns - 1), (float)row / (rows - 1));
                float height = water + .1f;
                Basin(new(.2f, 4.8f), new(2.9f, 3.9f));
                Basin(new(-1.6f, -2.4f), new(1.55f, 2.4f));
                Basin(new(1.2f, -9), new(1.4f, 2.9f));
                float exposure = 1 - Shelter.ShelterAt(new(p.X, height, p.Y));
                samples.Add(WetGroundSample3D.FromHeight(height, water, .85f, exposure, dampBand: .05f));

                void Basin(Vector2 center, Vector2 radius)
                {
                    Vector2 offset = (p - center) / radius;
                    float irregularity = .045f * MathF.Sin(p.X * 2.1f + p.Y * 1.3f);
                    height = Math.Min(height, water + .065f * (offset.LengthSquared() - 1 + irregularity));
                }
            }
        }
        return new(origin, size, columns, rows, water, samples);
    }
}

/// <summary>The gallery projects listener motion and the same ground query into ordinary engine audio.</summary>
internal sealed class RainStreetAudio : IDisposable
{
    private readonly AudioResourceScope resources = new();
    private readonly AurelianAudioRuntime runtime;
    private readonly EnvironmentalAmbience ambience;
    private Vector3? previous;
    private float stride;
    public int Footsteps { get; private set; }
    public string LastSurface { get; private set; } = "none";
    public Dictionary<string, int> SurfaceCounts { get; } = [];
    public EnvironmentalAmbienceState Ambience => ambience.State;

    public RainStreetAudio(bool audible)
    {
        IAudioOutputBackend output = new NullAudioOutputBackend();
        if (audible)
        {
            if (NAudioOutputBackend.TryCreate(out var device, out var error))
            {
                output = device!;
            }
            else
            {
                Console.WriteLine("Rain gallery audio device unavailable: " + error);
            }
        }
        runtime = new(resources, output);
        Add("rain-outside", ProceduralSoundKind.Rain, 1300, 2);
        Add("rain-inside", ProceduralSoundKind.ShelteredRain, 400, 2);
        Add("dry-step", ProceduralSoundKind.Footstep, 220, .22f);
        Add("wet-step", ProceduralSoundKind.Footstep, 110, .26f);
        Add("water-step", ProceduralSoundKind.WaterStep, 310, .35f);
        ambience = new(runtime, "street-weather", new("rain-outside"), new("rain-inside"));
    }

    public void Update(TimeSpan elapsed, Vector3 listener, bool raining, bool paused, bool focused, bool wetSurface, bool puddles)
    {
        runtime.Listener = new(new(listener.X, listener.Z), 10, 1, 20);
        runtime.SetFocused(focused);
        runtime.SetBusPaused(AudioBusId.Sfx, paused || !focused);
        ambience.Update(elapsed, raining ? 1 : 0, RainStreetWeather.Shelter.ShelterAt(listener), paused || !focused);
        if (!paused && focused && previous is { } prior)
        {
            float distance = Vector2.Distance(new(prior.X, prior.Z), new(listener.X, listener.Z));
            if (distance <= 4)
            {
                stride += distance;
                if (stride >= 1.3f)
                {
                    stride %= 1.3f;
                    var surface = RainStreetWeather.Ground.Sample(new(listener.X, listener.Z));
                    string sound = "dry-step";
                    if (puddles && surface.WaterDepth(RainStreetWeather.Ground.WaterLevel) > .003f)
                    {
                        sound = "water-step";
                    }
                    else if (wetSurface && surface.Wetness > .4f)
                    {
                        sound = "wet-step";
                    }
                    Footsteps++;
                    LastSurface = sound;
                    SurfaceCounts[sound] = SurfaceCounts.GetValueOrDefault(sound) + 1;
                    runtime.Play(new(new("gallery-step-" + Footsteps), new(sound), AudioBusId.Sfx));
                }
            }
        }
        previous = listener;
        runtime.Update(elapsed);
    }

    public void Dispose()
    {
        ambience.Dispose();
        runtime.Dispose();
        resources.Dispose();
    }

    public static object Qualify()
    {
        using var audio = new RainStreetAudio(audible: false);
        Vector2[] route = [new(-3.4f, 11), new(1.1f, 11), new(.2f, 4.8f)];
        void Visit(Vector2 xz)
        {
            float y = RainStreetWeather.Ground.Sample(xz).Height + 1.9f;
            audio.Update(TimeSpan.FromSeconds(.1), new(xz.X, y, xz.Y), true, false, true, true, true);
        }
        for (int index = 0; index < 30; index++) { Visit(route[0]); }
        var sheltered = audio.Ambience;
        for (int segment = 1; segment < route.Length; segment++)
        {
            for (int step = 1; step <= 40; step++)
            {
                Visit(Vector2.Lerp(route[segment - 1], route[segment], step / 40f));
            }
        }
        if (audio.SurfaceCounts.GetValueOrDefault("dry-step") == 0 || audio.SurfaceCounts.GetValueOrDefault("wet-step") == 0
            || audio.SurfaceCounts.GetValueOrDefault("water-step") == 0 || sheltered.ExteriorGain > .05f
            || sheltered.InteriorGain < .34f || audio.Ambience.ExteriorGain < .99f)
        {
            throw new InvalidOperationException("The shared weather field did not drive the expected audio route.");
        }
        return new { audio.Footsteps, audio.SurfaceCounts, Sheltered = sheltered, Exposed = audio.Ambience };
    }

    private void Add(string id, ProceduralSoundKind kind, float frequency, float duration)
    {
        resources.Add(new ProceduralSoundRecipe(kind, Seed: 73)
        {
            FrequencyHz = frequency, DurationSeconds = duration, Gain = .3f,
        }.Prepare(new(id)));
    }
}
