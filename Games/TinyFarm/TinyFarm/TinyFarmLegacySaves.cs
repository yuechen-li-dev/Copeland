using System.Text.Json;
using Deliverance.Core.Storage;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm;

/// <summary>
/// Saves from the last build before TinyFarm moved onto the shell: TinyFarm's own Deliverance slot
/// "sleeping-spring-a3" in %LOCALAPPDATA%\TinyFarm\saves. On first start the shell imports it into its empty menu slot
/// (see GameSaveSchema.Legacy). Earlier chapters' slots (gate A, A2) and the supper prototype's slot are not imported:
/// their worlds lack the shipping chest agent the current chapters build on.
/// </summary>
internal static class TinyFarmLegacySaves
{
    public const string Slot = "sleeping-spring-a3";

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TinyFarm", "saves");

    public static TinyFarmSemanticSaveSnapshot? Import(string directory, TinyFarmDefinitions current)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }
        var store = new FileSaveStore(directory);
        if (!store.ExistsAsync(Slot).GetAwaiter().GetResult())
        {
            return null;
        }
        var old = new TinyFarmGame(store, TinyFarmChapter.Shipping);
        if (!old.Load(Slot))
        {
            Console.Error.WriteLine("TinyFarm: the pre-shell save could not be read: " + old.Status);
            return null;
        }
        return TinyFarmChapterUpgrade.Upgrade(old.Persistence.CaptureSnapshot(), old.Definitions.Identity, current.Identity);
    }
}

/// <summary>
/// Carries a snapshot into a later chapter. Chapters only add content (recipes, objects, people) on top of the one
/// before, so an earlier chapter's world is valid in a later one once it names the later definitions. Use this in a
/// save migration when a new chapter ships.
/// </summary>
public static class TinyFarmChapterUpgrade
{
    public static TinyFarmSemanticSaveSnapshot Upgrade(TinyFarmSemanticSaveSnapshot snapshot, string fromIdentity, string toIdentity)
    {
        if (fromIdentity == toIdentity)
        {
            return snapshot;
        }
        if (!toIdentity.EndsWith(";" + fromIdentity, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A TinyFarm save can only move forward to a chapter built on its own.");
        }
        string json = JsonSerializer.Serialize(snapshot, TinyFarmDeliverancePersistence.SnapshotJson);
        // Every occurrence is a whole JSON string: the snapshot's definition hash and the world's definition set.
        string from = JsonSerializer.Serialize(fromIdentity);
        string to = JsonSerializer.Serialize(toIdentity);
        return JsonSerializer.Deserialize(json.Replace(from, to, StringComparison.Ordinal), TinyFarmDeliverancePersistence.SnapshotJson)
            ?? throw new InvalidDataException("The upgraded TinyFarm save was empty.");
    }
}
