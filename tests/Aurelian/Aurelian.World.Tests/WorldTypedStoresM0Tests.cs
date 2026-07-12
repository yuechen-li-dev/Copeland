using Aurelian.World.Stores;
using Aurelian.World.Units;
using Xunit;

namespace Aurelian.World.Tests;

public sealed class WorldTypedStoresM0Tests
{
    private static readonly UnitId Root = new(1);
    private static readonly UnitId Child = new(2);

    [Fact]
    public void UnitNameStore_Set_ReturnsNewStoreWithoutMutatingOriginal()
    {
        UnitNameStore original = UnitNameStore.Empty;

        UnitNameStore changed = original.Set(Root, new UnitName("Root"));

        Assert.NotSame(original, changed);
        Assert.False(original.TryGet(Root, out _));
        Assert.True(changed.TryGet(Root, out UnitName? name));
        Assert.Equal("Root", name.Value);
    }

    [Fact]
    public void UnitNameStore_Remove_ReturnsNewStoreWithoutMutatingOriginal()
    {
        UnitNameStore original = UnitNameStore.Empty.Set(Root, new UnitName("Root"));

        UnitNameStore changed = original.Remove(Root);

        Assert.NotSame(original, changed);
        Assert.True(original.TryGet(Root, out _));
        Assert.False(changed.TryGet(Root, out _));
    }

    [Fact]
    public void Transform2Store_GetOrIdentity_ReturnsIdentityWhenMissing()
    {
        Transform2 transform = Transform2Store.Empty.GetOrIdentity(Root);

        Assert.Equal(Transform2.Identity, transform);
    }

    [Fact]
    public void Transform2Store_Set_ReturnsNewStoreWithoutMutatingOriginal()
    {
        Transform2Store original = Transform2Store.Empty;
        Transform2 value = new(10, 20, 0.5, 2, 3);

        Transform2Store changed = original.Set(Root, value);

        Assert.NotSame(original, changed);
        Assert.False(original.TryGet(Root, out _));
        Assert.True(changed.TryGet(Root, out Transform2 transform));
        Assert.Equal(value, transform);
    }

    [Fact]
    public void WorldDataDocument_FromWorld_UsesEmptyStores()
    {
        WorldDocument world = CreateWorldDocument();

        WorldDataDocument document = WorldDataDocument.FromWorld(world);

        Assert.Same(world, document.World);
        Assert.Same(UnitNameStore.Empty, document.Names);
        Assert.Same(Transform2Store.Empty, document.Transforms);
    }

    [Fact]
    public void WorldDataSnapshot_IncludesNameAndTransformWhenPresent()
    {
        Transform2 childTransform = new(4, 5, 0.25, 1.5, 1.5);
        WorldDataDocument document = WorldDataDocument.FromWorld(CreateWorldDocument())
            .WithNames(UnitNameStore.Empty.Set(Child, new UnitName("Child")))
            .WithTransforms(Transform2Store.Empty.Set(Child, childTransform));

        WorldDataSnapshot snapshot = WorldDataSnapshotBuilder.Create(document);
        UnitDataSnapshot child = Assert.Single(snapshot.Units, x => x.Id == Child);

        Assert.Equal("Child", child.Name?.Value);
        Assert.Equal(childTransform, child.Transform);
        Assert.Equal(new UnitKindId("child"), child.Kind);
        Assert.Equal([Child], snapshot.World.GetImmediateChildren(Root).ToArray());
    }

    [Fact]
    public void WorldDataSnapshot_UsesIdentityTransformWhenMissing()
    {
        WorldDataDocument document = WorldDataDocument.FromWorld(CreateWorldDocument())
            .WithNames(UnitNameStore.Empty.Set(Root, new UnitName("Root")));

        WorldDataSnapshot snapshot = WorldDataSnapshotBuilder.Create(document);
        UnitDataSnapshot root = Assert.Single(snapshot.Units, x => x.Id == Root);

        Assert.Equal("Root", root.Name?.Value);
        Assert.Equal(Transform2.Identity, root.Transform);
    }

    private static WorldDocument CreateWorldDocument() =>
        new(Root, new Dictionary<UnitId, WorldUnitDescriptor>
        {
            [Root] = Descriptor(Root, "root", new UnitChild(Child)),
            [Child] = Descriptor(Child, "child")
        });

    private static WorldUnitDescriptor Descriptor(UnitId id, string kind, params UnitChild[] children) =>
        new(id, new UnitKindId(kind), new UnitComposition(children));
}
