using System.Security.Cryptography;
using System.Text;

namespace TinyFarm.Core;

public static class TinyFarmSemanticHash
{
    public static string Compute(TinyFarmState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var canonical = new StringBuilder();
        canonical.Append("v=").Append(state.Version)
            .Append(";minute=").Append(state.Minute)
            .Append(";favor=").Append(state.Favor).AppendLine();

        foreach (ActorState actor in state.Actors.OrderBy(actor => actor.Id.Value, StringComparer.Ordinal))
        {
            canonical.Append("actor|").Append(actor.Id.Value)
                .Append('|').Append(actor.Name)
                .Append('|').Append(actor.Location.Value)
                .Append('|').Append(actor.Money)
                .Append('|').Append(actor.IsPlayer ? '1' : '0')
                .Append('|').AppendJoin(',', actor.Inventory
                    .OrderBy(item => item.Value, StringComparer.Ordinal)
                    .Select(item => item.Value))
                .AppendLine();
        }

        foreach (ItemState item in state.Items.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            canonical.Append("item|").Append(item.Id.Value)
                .Append('|').Append(item.Name)
                .Append('|').Append(item.Price)
                .Append('|').Append(item.Owner?.Value ?? "-")
                .Append('|').Append(item.GroundLocation?.Value ?? "-")
                .AppendLine();
        }

        canonical.Append("facts|").AppendJoin(',', state.Facts.OrderBy(fact => fact));
        byte[] bytes = Encoding.UTF8.GetBytes(canonical.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
