using System.Numerics;
using Aurelian.Audio;
using TinyFarm.Core;

namespace TinyFarm.Runtime;

public static class TinyFarmAudioAssets
{
    public static AudioAssetId SwordSwing { get; } = new("tinyfarm.sfx.sword-swing");
    public static AudioAssetId Pickup { get; } = new("tinyfarm.sfx.pickup");
    public static AudioAssetId Footstep { get; } = new("tinyfarm.sfx.footstep");
    public static AudioAssetId Harvest { get; } = new("tinyfarm.sfx.harvest-pop");
    public static AudioAssetId FarmMusic { get; } = new("tinyfarm.music.farm-day");
    public static AudioAssetId RiverAmbient { get; } = new("tinyfarm.ambient.river");
}

public sealed class TinyFarmAudioProjector
{
    public IReadOnlyList<AudioCue> Project(IEnumerable<IntentResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var cues = new List<AudioCue>();
        foreach (IntentResult result in results)
        {
            if (result.Status != IntentResultStatus.Accepted)
            {
                continue;
            }
            for (int eventIndex = 0; eventIndex < result.Events.Count; eventIndex++)
            {
                GameEvent gameEvent = result.Events[eventIndex];
                AudioAssetId? asset = AssetFor(gameEvent.Kind);
                if (asset is null)
                {
                    continue;
                }
                AudioEventId eventId = new($"tinyfarm:{result.Envelope.Sequence}:{eventIndex}:{gameEvent.Kind}");
                Vector2? position = gameEvent.Kind == GameEventKind.ActorMoved
                    ? null
                    : PositionFor(gameEvent);
                cues.Add(new AudioCue(eventId, asset.Value, AudioBusId.Sfx, Position: position));
            }
        }
        return cues;
    }

    public AudioCue FarmMusic(AudioEventId eventId) =>
        new(eventId, TinyFarmAudioAssets.FarmMusic, AudioBusId.Music, Loop: true, FadeIn: TimeSpan.FromSeconds(1));

    public AudioCue RiverAmbient(AudioEventId eventId, Vector2 position) =>
        new(eventId, TinyFarmAudioAssets.RiverAmbient, AudioBusId.Ambient, Volume: 0.7f, Loop: true, Position: position);

    private static AudioAssetId? AssetFor(GameEventKind kind) => kind switch
    {
        GameEventKind.EnemyDefeated => TinyFarmAudioAssets.SwordSwing,
        GameEventKind.ItemTaken => TinyFarmAudioAssets.Pickup,
        GameEventKind.CropHarvested or GameEventKind.ForageGathered => TinyFarmAudioAssets.Harvest,
        GameEventKind.ActorMoved => TinyFarmAudioAssets.Footstep,
        _ => null
    };

    private static Vector2? PositionFor(GameEvent gameEvent)
    {
        return gameEvent.SceneObject is null && gameEvent.Enemy is null && gameEvent.Item is null
            ? null
            : Vector2.Zero;
    }
}
