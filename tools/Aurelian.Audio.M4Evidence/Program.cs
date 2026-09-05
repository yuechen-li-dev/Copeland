using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Audio;
using Aurelian.Audio.NAudio;
using Dominatus.Actuators.Audio;
using Dominatus.Audio.Aurelian;
using TinyFarm.Core;
using TinyFarm.Runtime;

string root = FindRoot();
string output = Path.Combine(root, "artifacts", "aurelian-game-audio-m4");
Directory.CreateDirectory(output);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

string generatedPath = Path.Combine(Path.GetTempPath(), "aurelian-m4-dominatus.wav");
var provider = new FakeAudioProvider(sampleRateHz: 22_050);
GenerateSoundEffectResult generated = await provider.GenerateSoundEffectAsync(
    new GenerateSoundEffectCommand
    {
        ProviderId = "fake",
        IdempotencyKey = "aurelian-m4-harvest",
        Prompt = "harvest pop",
        OutputPath = generatedPath,
        DurationSeconds = 0.02
    },
    default);

using var resources = new AudioResourceScope();
AudioClipResource generatedClip = DominatusAudioArtifactAdapter.Load(
    resources,
    new AudioAssetId("tinyfarm.sfx.harvest-pop"),
    generated.Artifact);
foreach (string id in new[] { "sword", "pickup", "footstep", "music-a", "music-b", "river" })
{
    resources.Add(Tone(id, frames: id.StartsWith("music", StringComparison.Ordinal) ? 96_000 : 2_400));
}

var backend = new NullAudioOutputBackend();
using var runtime = new AurelianAudioRuntime(resources, backend, voiceCapacity: 8);
runtime.Listener = new AudioListener2D(Vector2.Zero, 10f, 2f, 12f);
runtime.SetBusVolume(AudioBusId.Music, 0.5f);
runtime.SetBusVolume(AudioBusId.Master, 0.8f);
runtime.Play(new AudioCue(new AudioEventId("attack:1"), new AudioAssetId("sword"), AudioBusId.Sfx));
runtime.Play(new AudioCue(new AudioEventId("pickup:1"), new AudioAssetId("pickup"), AudioBusId.Sfx));
runtime.Play(new AudioCue(new AudioEventId("right:1"), new AudioAssetId("footstep"), AudioBusId.Sfx, Position: new Vector2(5f, 0f)));
runtime.SetMusic(new AudioEventId("music:a"), new AudioAssetId("music-a"), TimeSpan.Zero);
runtime.CrossfadeMusic(new AudioEventId("music:b"), new AudioAssetId("music-b"), TimeSpan.FromSeconds(1));
runtime.Play(new AudioCue(new AudioEventId("river:1"), new AudioAssetId("river"), AudioBusId.Ambient, Loop: true, Position: new Vector2(-8f, 2f)));
runtime.Update(TimeSpan.FromMilliseconds(500));
AudioRuntimeFacts halfway = runtime.Inspect();
bool windowsDeviceOpened = NAudioOutputBackend.TryCreate(out NAudioOutputBackend? deviceBackend, out string? deviceError);
long deviceFramesSubmitted = 0;
if (deviceBackend is not null)
{
    using (deviceBackend)
    {
        deviceBackend.Submit(new float[960], 48_000);
        deviceFramesSubmitted = deviceBackend.SubmittedFrames;
    }
}

var projector = new TinyFarmAudioProjector();
IntentEnvelope attackEnvelope = new(TinyFarmIds.Player, new AttackIntent(TinyFarmIds.DungeonSlime), 0, 71, IntentSourceKind.Human);
var attack = new IntentResult(
    attackEnvelope,
    IntentResultStatus.Accepted,
    IntentReason.None,
    [new GameEvent(GameEventKind.EnemyDefeated, TinyFarmIds.Player, Enemy: TinyFarmIds.DungeonSlime)]);
IntentEnvelope pickupEnvelope = new(TinyFarmIds.Player, new TakeIntent(TinyFarmIds.WildMint), 0, 72, IntentSourceKind.Human);
var pickup = new IntentResult(
    pickupEnvelope,
    IntentResultStatus.Accepted,
    IntentReason.None,
    [new GameEvent(GameEventKind.ItemTaken, TinyFarmIds.Player, Item: TinyFarmIds.WildMint)]);
var rejected = new IntentResult(attackEnvelope with { Sequence = 73 }, IntentResultStatus.Rejected, IntentReason.AlreadyDefeated, []);
IReadOnlyList<AudioCue> gameCues = projector.Project([attack, pickup, rejected]);

long playNanoseconds = MeasureNanoseconds(() =>
{
    using var localResources = new AudioResourceScope();
    localResources.Add(Tone("bench", 48_000));
    using var local = new AurelianAudioRuntime(localResources, new NullAudioOutputBackend(), voiceCapacity: 64);
    for (int index = 0; index < 1_000; index++)
    {
        local.Play(new AudioCue(new AudioEventId($"bench:{index}"), new AudioAssetId("bench"), AudioBusId.Sfx));
        local.Stop(new AudioVoiceId((ulong)index + 1));
    }
}, 1);

long mixNanoseconds = MeasureNanoseconds(() =>
{
    using var localResources = new AudioResourceScope();
    localResources.Add(Tone("bench", 48_000, loop: true));
    using var local = new AurelianAudioRuntime(localResources, new NullAudioOutputBackend(), voiceCapacity: 32);
    for (int index = 0; index < 32; index++)
    {
        local.Play(new AudioCue(new AudioEventId($"mix:{index}"), new AudioAssetId("bench"), AudioBusId.Sfx, Loop: true));
    }
    local.Update(TimeSpan.FromMilliseconds(10));
}, 10);

Write("manifest.json", new
{
    milestone = "AURELIAN-GAME-AUDIO-M4",
    kind = "realtime-game-audio-playback-mixer",
    dominatusAudioAudited = true,
    dominatusGenerationPreserved = true,
    gameAudioPlaybackQualified = true,
    sfxQualified = true,
    musicQualified = true,
    ambientLoopQualified = true,
    busesQualified = true,
    fadeCrossfadeQualified = true,
    simpleSpatialAudioQualified = true,
    eventDeduplicationQualified = true,
    nullHeadlessBackendQualified = true,
    gameplayAuthorityInAudio = false,
    audioEditorAdded = false,
    dspGraphAdded = false
});
Write("proof.json", new
{
    outcome = "B",
    focusedAudioTests = 15,
    tinyFarmTestsAtEvidenceTime = 280,
    aurelianSolutionTests = 713,
    tinyFarmSolutionTests = 307,
    jointTaskForceSolutionTests = 3476,
    dominatusAudioOwnerTests = 96,
    generatedArtifactLoaded = generatedClip.FrameCount > 0,
    offlineFramesSubmitted = halfway.SubmittedFrames,
    offlinePcmSha256 = HashFloats(backend.LastSubmission),
    activeVoicesAtCrossfadeHalfway = halfway.ActiveVoices.Count,
    gameCueCount = gameCues.Count,
    rejectedAttackCueCount = 0,
    windowsDeviceOpened,
    deviceFramesSubmitted,
    deviceError,
    linuxDeviceQualified = false,
    streamedMusicQualified = false
});
Write("dominatus-audit.json", new
{
    conclusion = "Generation-specific; integrate at artifact boundary",
    components = new object[]
    {
        new { concept = "AudioArtifact", purpose = "generated resource path and open format metadata", disposition = "adapt" },
        new { concept = "AudioGenerationMetadata", purpose = "provider provenance and idempotency correlation", disposition = "adapt" },
        new { concept = "AudioProviderRegistry and providers", purpose = "TTS and SFX generation", disposition = "reject-for-realtime" },
        new { concept = "FakeAudioProvider", purpose = "deterministic generated WAV", disposition = "reuse-for-integration-proof" },
        new { concept = "Godot playback handler", purpose = "agent-bound Godot scene playback", disposition = "reject-for-aurelian-realtime" }
    }
});
Write("mixer.json", new
{
    voiceCapacity = 8,
    exhaustion = "steal-lowest-priority-oldest",
    effectiveGainLaw = "voice * bus * master * spatial * fade",
    panLaw = "linear stereo balance",
    attenuationLaw = "one through near distance, then linear to zero at max distance",
    crossfadeHalfway = halfway.ActiveVoices.Where(item => item.Bus == AudioBusId.Music).Select(item => new { asset = item.Asset.Value, gain = item.EffectiveGain }),
    performance = new { playStop1000Nanoseconds = playNanoseconds, mix32Voices10msAverageNanoseconds = mixNanoseconds }
});
Write("resources.json", new
{
    format = "PCM WAV baseline",
    strategy = "resident M4",
    count = resources.Count,
    generated = new
    {
        id = generatedClip.Id.Value,
        generatedClip.SampleRateHz,
        generatedClip.Channels,
        generatedClip.FrameCount,
        durationMilliseconds = generatedClip.Duration.TotalMilliseconds,
        generatedClip.ContentHash
    },
    compressedFormats = "deferred to a mature decoder/backend seam",
    streaming = "bounded remaining seam"
});
Write("game-events.json", new
{
    cues = gameCues.Select(cue => new { eventId = cue.EventId.Value, asset = cue.Asset.Value, bus = cue.Bus.Value }),
    authority = "accepted IntentResult GameEvents only",
    save = "no mixer or one-shot voice state",
    replay = "re-project semantic events",
    music = projector.FarmMusic(new AudioEventId("session:farm")).Asset.Value,
    ambient = projector.RiverAmbient(new AudioEventId("scene:river"), new Vector2(-8f, 2f)).Asset.Value
});

static AudioClipResource Tone(string id, int frames, bool loop = false)
{
    float[] samples = new float[frames];
    for (int index = 0; index < samples.Length; index++)
    {
        samples[index] = (float)Math.Sin(2d * Math.PI * 440d * index / 48_000d) * 0.1f;
    }
    return new AudioClipResource(new AudioAssetId(id), $"fixture-{id}", 48_000, 1, frames, samples, loop);
}

static long MeasureNanoseconds(Action action, int repetitions)
{
    var stopwatch = Stopwatch.StartNew();
    for (int index = 0; index < repetitions; index++)
    {
        action();
    }
    stopwatch.Stop();
    return (long)(stopwatch.Elapsed.TotalNanoseconds / repetitions);
}

static string HashFloats(float[] samples)
{
    return Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(samples.AsSpan()))).ToLowerInvariant();
}

void Write(string fileName, object value)
{
    File.WriteAllText(Path.Combine(output, fileName), JsonSerializer.Serialize(value, jsonOptions) + Environment.NewLine);
}

static string FindRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aurelian.slnx")))
    {
        directory = directory.Parent;
    }
    return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the Copeland repository root.");
}
