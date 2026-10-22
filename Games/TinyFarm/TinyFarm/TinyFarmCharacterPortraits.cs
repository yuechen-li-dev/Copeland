using Aurelian.GameWorld2D;
using TinyFarm.Core;

namespace TinyFarm.Native;

/// <summary>Character identity art. Human versus scheduled control does not select a different portrait.</summary>
internal sealed class TinyFarmCharacterPortraits
{
    private readonly Dictionary<string, SpriteAtlasResource> resources = new(StringComparer.Ordinal);

    public TinyFarmCharacterPortraits(string assetDirectory)
    {
        Add(assetDirectory, TinyFarmIds.Player.Value, "gardener-v1",
            "3ee1633f6a08225f7386b8ea7554c312a700cedd75317ced559dc6c6d320a812");
        Add(assetDirectory, TinyFarmIds.Mara.Value, "mara-v1",
            "ac55e0807d72338c22491e35c19bce6e4db3ca7f74bc9ff96e6210a6a1b0e0e8");
        Add(assetDirectory, TinyFarmIds.Elias.Value, "elias-v1",
            "d9fe84cbc6a863948054b3e3ff2e191290da1e22799c87e4a8d63ae8a6520490");
        Add(assetDirectory, TinyFarmIds.Sela.Value, "sela-v1",
            "b4ad07471e75fde9865ff7918e9ed2d6e86c8ded01b0c4882a4cb14a7e90c824");
    }

    public bool TryGet(ActorState actor, out SpriteAtlasResource resource)
    {
        if (TinyFarmAgentPolicy.IsObject(actor))
        {
            resource = null!;
            return false;
        }
        // Reuse the existing authored conversation-sprite key when supplied. Otherwise identity is the stable actor ID,
        // including the Gardener's current legacy ID "player". Do not choose by IsPlayer or controller kind.
        string key = actor.Agent?.Appearance.ConversationSprite ?? actor.Id.Value;
        return resources.TryGetValue(key, out resource!);
    }

    private void Add(string assetDirectory, string key, string folder, string fileHash)
    {
        resources.Add(key, TinyFarmM24Assets.LoadResource("portrait:" + key,
            Path.Combine(assetDirectory, "characters", folder, "portrait.png"), fileHash, SpriteSampling.Linear));
    }
}
