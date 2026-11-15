using System.Numerics;
using Aurelian.Audio;
using Aurelian.Games;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Runtime;
using Aurelian.Spatial3D;

namespace Aurelian.Beacon3D;

/// <summary>Accepted game state projects into motion, sound and bounded water events; no reverse dependency.</summary>
public sealed class BeaconPresentation : IDisposable
{
    private readonly FirstPersonPresentation3D motion = new();
    private GameContext? context;
    private EnvironmentalAmbience? ambience;
    private LayeredMusic? music;
    private readonly List<WaterImpulse3D> impacts = [];
    public bool Enabled { get; set; } = true;
    public bool GroundWeather { get; set; } = true;
    public FirstPersonPose3D Pose => Enabled ? motion.Pose : motion.PoseWithoutMotion;
    public FirstPersonPresentation3D Motion => motion;
    public IReadOnlyList<WaterImpulse3D> Impacts => impacts;
    public EnvironmentalAmbienceState AudioState => ambience?.State ?? default;
    public Dictionary<string, int> Sounds { get; } = [];
    public float WeaponClearance { get; private set; } = 1.05f;
    public float MusicIntensity { get; private set; }
    public LayeredMusicFacts? MusicState => music?.Inspect();

    public void Start(GameContext services)
    {
        context = services;
        Add("outside", ProceduralSoundKind.Wind, 800, 3, .4f);
        Add("inside", ProceduralSoundKind.Wind, 130, 3, .4f);
        Add("dry", ProceduralSoundKind.Footstep, 220, .22f, .45f);
        Add("wet", ProceduralSoundKind.Footstep, 110, .26f, .45f);
        Add("water", ProceduralSoundKind.WaterStep, 310, .35f, .45f);
        Add("shot", ProceduralSoundKind.MetalImpact, 95, .15f, .65f);
        Add("reload", ProceduralSoundKind.MetalImpact, 450, .18f, .3f);
        Add("land", ProceduralSoundKind.Footstep, 70, .3f, .5f);
        ambience = new(services.Audio, "beacon-environment", new("beacon.outside"), new("beacon.inside"));
        foreach (var clip in BeaconMusic.Prepare())
        {
            services.AudioResources.Add(clip);
        }
        music = services.Audio.PlayMusicLayers(new("beacon.score"),
            [new(new("beacon.music.base"), .55f), new(new("beacon.music.combat"), 0)])
            ?? throw new InvalidOperationException("Beacon score could not acquire a music voice.");
    }

    public void Reset(BeaconGame game)
    {
        WeaponClearance = FirstPersonWeaponProbe3D.Measure(game.SpatialWorld, game.Eye, game.Direction,
            reach: motion.Profile.WeaponReachMetres);
        motion.Reset(Input(game, false, false));
        Enabled = true;
        ambience?.Reset();
        impacts.Clear();
        Sounds.Clear();
        MusicIntensity = 0;
        music?.Reset();
        context?.Audio.StopBus(AudioBusId.Sfx, TimeSpan.Zero);
    }

    public void Step(BeaconGame game, bool aiming, bool sprinting, float seconds, int oldShots, bool wasReloading)
    {
        WeaponClearance = FirstPersonWeaponProbe3D.Measure(game.SpatialWorld, game.Eye, game.Direction, motion.Pose.Aim,
            reach: motion.Profile.WeaponReachMetres);
        int steps = motion.Footsteps;
        int landings = motion.Landings;
        motion.Step(Input(game, aiming, sprinting), seconds);
        var creatures = game.Creatures.Where(agent => agent.State.Health > 0).ToArray();
        float nearest = creatures.Length == 0 ? 12 : creatures.Min(agent => Vector3.Distance(agent.State.Position, game.Eye));
        MusicIntensity = Math.Clamp(creatures.Length / 4f * .5f + Math.Max(0, 1 - nearest / 12) * .5f
            + (game.Hurt ? .25f : 0), 0, 1);
        music?.SetGains([.55f, MusicIntensity * .65f], TimeSpan.FromSeconds(.4));
        if (game.Shots > oldShots)
        {
            Sound("shot");
        }
        if (game.Reloading && !wasReloading)
        {
            Sound("reload");
        }
        if (motion.Landings > landings && motion.LastLandingSpeed > 1)
        {
            Sound("land");
        }
        if (motion.Footsteps > steps)
        {
            var sample = GroundWeather && game.Height < .2f ? BeaconEnvironment.SurfaceAt(game.Position) : new(0, 0, 1);
            string surface = "dry";
            if (sample.WaterDepth(BeaconEnvironment.Ground.WaterLevel) > .003f && GroundWeather && game.Height < .2f)
            {
                surface = "water";
            }
            else if (sample.Wetness > .4f)
            {
                surface = "wet";
            }
            Sound(surface);
            if (surface == "water")
            {
                if (impacts.Count == 8)
                {
                    impacts.RemoveAt(0);
                }
                impacts.Add(new(game.Position, game.Time, .002f, .18f, .5f, 1));
            }
        }
        impacts.RemoveAll(impact => game.Time > impact.StartedAt + impact.Lifetime);
    }

    public void Frame(BeaconGame game, GameFrame frame)
    {
        if (context is null)
        {
            return;
        }
        context.Audio.Listener = new(game.Position, 10, 1, 22);
        ambience!.Update(frame.Elapsed, 1, BeaconEnvironment.Shelter.ShelterAt(game.Eye), !frame.Playing || game.Dead || game.Won);
        music?.SetPaused(!frame.Playing || game.Dead || game.Won);
    }

    public void Dispose()
    {
        music?.Dispose();
        ambience?.Dispose();
    }

    private FirstPersonMotion3D Input(BeaconGame game, bool aiming, bool sprinting) => new(
        new(game.Position.X, game.Height, game.Position.Y), game.Yaw, game.Pitch,
        game.LastMovement?.Grounded ?? game.Height == 0, game.Shots, aiming, sprinting, game.Reloading, WeaponClearance);

    private void Sound(string sound)
    {
        Sounds[sound] = Sounds.GetValueOrDefault(sound) + 1;
        context?.PlaySound("beacon." + sound);
    }

    private void Add(string id, ProceduralSoundKind kind, float frequency, float duration, float gain)
    {
        context!.AudioResources.Add(new ProceduralSoundRecipe(kind, 73)
        {
            FrequencyHz = frequency, DurationSeconds = duration, Gain = gain,
        }.Prepare(new("beacon." + id)));
    }
}
