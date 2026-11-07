using Marionette.Skyrim;
using System.Text.Json.Serialization;

namespace Marionette.Skyrim.App;

public sealed record SkyrimCharacterLocation(string CellPluginName, uint CellLocalFormId,
    float PositionX, float PositionY, float PositionZ)
{
    public void Validate()
    {
        _ = new SkyrimPlacedActorOrigin(CellPluginName, CellLocalFormId);
        if (!float.IsFinite(PositionX) || !float.IsFinite(PositionY) || !float.IsFinite(PositionZ) ||
            Math.Abs(PositionX) > 1_000_000 || Math.Abs(PositionY) > 1_000_000 || Math.Abs(PositionZ) > 1_000_000)
        {
            throw new InvalidDataException("character_location_invalid");
        }
    }
}

public sealed record BodyCharacterInfo(uint FormId, string PluginName, uint LocalFormId, string Name,
    string LocationName, string CellPluginName, uint CellLocalFormId, float PositionX, float PositionY,
    float PositionZ, bool Loaded, string UnavailableReason)
{
    [JsonIgnore]
    public SkyrimCharacterLocation? Location => CellLocalFormId == 0 || string.IsNullOrEmpty(CellPluginName)
        ? null : new(CellPluginName, CellLocalFormId, PositionX, PositionY, PositionZ);
}

public sealed partial class SkyrimBodyControl
{
    public async ValueTask<BodyCharacterInfo> DescribeCharacterAsync(SkyrimPlacedActorOrigin origin,
        ulong worldGeneration, CancellationToken cancellationToken = default)
    {
        var request = CharacterRequest("describe", origin, worldGeneration, 0);
        BodyControlResult result = await SendResultAsync(request, cancellationToken).ConfigureAwait(false);
        BodyCharacterInfo info = result.Character ?? throw new InvalidDataException("character_receipt_missing");
        if (result.WorldGeneration != worldGeneration || info.FormId is 0 or 20 ||
            new SkyrimPlacedActorOrigin(info.PluginName, info.LocalFormId) != origin)
        {
            throw new InvalidDataException("character_receipt_invalid");
        }
        info.Location?.Validate();
        return info;
    }

    /// <summary>Moves only the player streaming anchor; acquisition remains a separate, observed operation.
    /// A location hint warms the last known cell when the original reference is unloaded.</summary>
    public async ValueTask TravelToCharacterAsync(SkyrimPlacedActorOrigin origin, SkyrimCharacterLocation? hint,
        ulong worldGeneration, CancellationToken cancellationToken = default)
    {
        hint?.Validate();
        SkyrimBodyLease? current = Lease;
        if (current is not null && current.WorldGeneration != worldGeneration)
        {
            throw new ArgumentException("body_world_generation_changed");
        }
        BodyControlRequest request = CharacterRequest("travel", origin, worldGeneration, current?.LeaseId ?? 0) with
        {
            BodyCellPlugin = hint?.CellPluginName ?? "",
            BodyCellLocalFormId = hint?.CellLocalFormId ?? 0,
            TargetPositionX = hint?.PositionX ?? 0,
            TargetPositionY = hint?.PositionY ?? 0,
            TargetPositionZ = hint?.PositionZ ?? 0,
        };
        BodyControlResult result = await SendResultAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.WorldGeneration != worldGeneration || result.BodyControl.LeaseId != 0)
        {
            throw new InvalidDataException("character_travel_receipt_invalid");
        }
    }

    private static BodyControlRequest CharacterRequest(string action, SkyrimPlacedActorOrigin origin,
        ulong generation, ulong lease)
    {
        if (generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));
        origin = new SkyrimPlacedActorOrigin(origin.PluginName, origin.LocalFormId);
        return new(1, "body_control", Guid.NewGuid().ToString("N"), 2000, generation, lease, 0, action,
            BodyPluginName: origin.PluginName, BodyLocalFormId: origin.LocalFormId);
    }
}
