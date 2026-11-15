using System.Text.Json;
using System.Runtime.InteropServices;
using Aurelian.Audio;
using Aurelian.Beacon3D;
using Aurelian.Games;

internal static class MusicLayersProof
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        string evidence = Path.Combine(output, "evidence.json");
        File.WriteAllText(evidence, "{\"Accepted\":false}");
        using var session = new GameSession(new BeaconShellGame(), Path.Combine(output, "saves"));
        using var resources = new AudioResourceScope();
        foreach (string asset in new[] { "beacon.music.base", "beacon.music.combat" })
        {
            resources.Add(session.Context.AudioResources.Get(new(asset)));
        }
        var backend = new NullAudioOutputBackend();
        using var mixer = new AurelianAudioRuntime(resources, backend);
        using var music = mixer.PlayMusicLayers(new("qualified-score"),
            [new(new("beacon.music.base"), .55f), new(new("beacon.music.combat"), 0)])!;
        mixer.Update(TimeSpan.FromSeconds(8));
        float[] calm = backend.LastSubmission;
        WriteWav(Path.Combine(output, "beacon-calm.wav"), calm);
        music.Reset();
        music.SetGains([.55f, .65f], TimeSpan.FromSeconds(.4));
        mixer.Update(TimeSpan.FromSeconds(8));
        float[] combat = backend.LastSubmission;
        WriteWav(Path.Combine(output, "beacon-combat.wav"), combat);
        double calmRms = Rms(calm);
        double combatRms = Rms(combat);
        Require(calmRms > .001 && combatRms > calmRms * 1.03, "Combat layer did not add audible energy.");
        double phase = music.Inspect().SourceFrame;
        music.SetPaused(true);
        mixer.Update(TimeSpan.FromSeconds(.1));
        Require(phase == music.Inspect().SourceFrame && backend.LastSubmission.All(sample => sample == 0),
            "Paused score advanced or emitted audio.");
        music.Reset();
        mixer.Update(TimeSpan.FromSeconds(8));
        Require(MemoryMarshal.AsBytes(calm.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(backend.LastSubmission.AsSpan())),
            "Restart did not reproduce the initial mixed PCM exactly.");
        mixer.SetBusVolume(AudioBusId.Music, 0);
        mixer.Update(TimeSpan.FromSeconds(.1));
        Require(backend.LastSubmission.All(sample => sample == 0), "Music volume zero did not mute the score.");
        File.WriteAllText(evidence, JsonSerializer.Serialize(new
        {
            Accepted = true,
            FramesPerLayer = 384_000,
            SharedCursor = true,
            LogicalVoices = mixer.Inspect().ActiveVoices.Count,
            CalmRms = calmRms,
            CombatRms = combatRms,
            RestartPcmIdentical = true,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("MUSIC_LAYERS_ACCEPTED " + evidence);
    }

    private static double Rms(float[] samples) => Math.Sqrt(samples.Sum(sample => (double)sample * sample) / samples.Length);

    private static void WriteWav(string path, float[] samples)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + samples.Length * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(48_000);
        writer.Write(48_000 * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples.Length * 2);
        foreach (float sample in samples)
        {
            writer.Write((short)Math.Round(Math.Clamp(sample, -1, 1) * short.MaxValue));
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
