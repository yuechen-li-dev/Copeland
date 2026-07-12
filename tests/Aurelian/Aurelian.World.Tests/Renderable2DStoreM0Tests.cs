using Aurelian.World.Stores;
using Aurelian.World.Units;
using Xunit;

namespace Aurelian.World.Tests;

public sealed class Renderable2DStoreM0Tests
{
    private static readonly UnitId Root = new(1);
    private static readonly UnitId Child = new(2);

    [Fact]
    public void Renderable2DStore_Set_ReturnsNewStoreWithoutMutatingOriginal()
    {
        Renderable2DStore original = Renderable2DStore.Empty;
        Renderable2DData renderable = Renderable("mesh/child", "material/child", sortOrder: 3);

        Renderable2DStore changed = original.Set(Child, renderable);

        Assert.NotSame(original, changed);
        Assert.False(original.TryGet(Child, out _));
        Assert.True(changed.TryGet(Child, out Renderable2DData stored));
        Assert.Equal(renderable, stored);
    }

    [Fact]
    public void Renderable2DStore_Remove_ReturnsNewStoreWithoutMutatingOriginal()
    {
        Renderable2DStore original = Renderable2DStore.Empty.Set(Child, Renderable("mesh/child", "material/child"));

        Renderable2DStore changed = original.Remove(Child);

        Assert.NotSame(original, changed);
        Assert.True(original.TryGet(Child, out _));
        Assert.False(changed.TryGet(Child, out _));
    }

    [Fact]
    public void WorldDataDocument_FromWorld_UsesEmptyRenderableStore()
    {
        WorldDocument world = CreateWorldDocument();

        WorldDataDocument document = WorldDataDocument.FromWorld(world);

        Assert.Same(world, document.World);
        Assert.Same(Renderable2DStore.Empty, document.Renderables);
    }

    [Fact]
    public void WorldDataSnapshot_IncludesRenderableWhenPresent()
    {
        Renderable2DData renderable = Renderable("mesh/child", "material/child", sortOrder: 9);
        WorldDataDocument document = WorldDataDocument.FromWorld(CreateWorldDocument())
            .WithRenderables(Renderable2DStore.Empty.Set(Child, renderable));

        WorldDataSnapshot snapshot = WorldDataSnapshotBuilder.Create(document);
        UnitDataSnapshot child = Assert.Single(snapshot.Units, x => x.Id == Child);

        Assert.Equal(renderable, child.Renderable);
    }

    private static Renderable2DData Renderable(string mesh, string material, bool visible = true, int sortOrder = 0) =>
        new(new WorldMeshRef(mesh), new WorldMaterialRef(material), visible, sortOrder);

    private static WorldDocument CreateWorldDocument() =>
        new(Root, new Dictionary<UnitId, WorldUnitDescriptor>
        {
            [Root] = Descriptor(Root, "root", new UnitChild(Child)),
            [Child] = Descriptor(Child, "child")
        });

    private static WorldUnitDescriptor Descriptor(UnitId id, string kind, params UnitChild[] children) =>
        new(id, new UnitKindId(kind), new UnitComposition(children));
}
