using Aurelian.Assets.ArtSlots;
using TinyFarm.Native;
using Xunit;

namespace TinyFarm.Core.Tests;

/// <summary>TinyFarm's art slots load, realize, and name things that exist in the Sleeping Spring.</summary>
public sealed class TinyFarmArtSlotTests
{
    private static string Manifest => Path.Combine(AppContext.BaseDirectory, "Assets", "art", "art-slots.json");

    [Fact]
    public void EverySlotRealizesAtItsDeclaredSizeAndImportsAsSprites()
    {
        var slots = new TinyFarmArtSlots(Manifest);
        foreach (ArtSlotDefinition slot in slots.Catalog.Slots)
        {
            ArtSlotImage image = slots.Catalog.Realize(slot.Id);
            Assert.Equal((double)slot.Width / slot.Height, (double)image.Width / image.Height, 2);
            Assert.True(slots.TryGet(slot.Id, out var resource));
            Assert.Equal(slot.Columns * slot.Rows, Enumerable.Range(0, slot.Columns * slot.Rows)
                .Select(index => slots.Frame(resource.Id, index.ToString())).Count());
        }
    }

    [Fact]
    public void SlotIdsNameRealObjectsItemsAndActors()
    {
        TinyFarmDefinitions definitions = TinyFarmSupperContent.Load();
        TinyFarmState state = TinyFarmSupperContent.Start(definitions);
        var objects = definitions.Scenes.All.SelectMany(scene => scene.Objects).Select(item => item.Id.Value).ToHashSet();
        var nodes = definitions.ForageNodes.Select(node => node.Id.Value).ToHashSet();
        var items = state.Items.Select(item => item.Id.Value).ToHashSet();
        var actors = state.Actors.Select(actor => actor.Id.Value).ToHashSet();
        foreach (ArtSlotDefinition slot in ArtSlotCatalog.Load(Manifest).Slots)
        {
            string[] parts = slot.Id.Split('.', 2);
            string name = parts[1].EndsWith(".depleted", StringComparison.Ordinal) ? parts[1][..^".depleted".Length] : parts[1];
            bool known = parts[0] switch
            {
                "object" => objects.Contains(name),
                "forage" => nodes.Contains(name),
                "item" => items.Contains(name),
                "actor" => actors.Contains(name),
                "prop" => true,
                _ => false,
            };
            Assert.True(known, $"Art slot '{slot.Id}' does not name anything in the Sleeping Spring.");
        }
    }
}
