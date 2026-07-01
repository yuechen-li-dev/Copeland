using Aurelian.Actuation.World;
using Aurelian.Actuation.World.Requests;
using Aurelian.World.Stores;
using Aurelian.World.Units;
using Xunit;

namespace Aurelian.Actuation.Tests;

public sealed class Renderable2DActuatorM0Tests
{
    private static readonly UnitId Root = new(1);
    private static readonly UnitId Child = new(2);
    private static readonly UnitId Missing = new(999);

    [Fact]
    public void WorldDataActuator_SetRenderable_AppliesToExistingUnit()
    {
        WorldDataDocument original = WorldDataDocument.FromWorld(CreateWorldDocument());
        Renderable2DData renderable = Renderable("mesh/child", "material/child", sortOrder: 2);

        WorldActuationResult<WorldDataDocument> result = WorldDataActuator.Apply(
            original,
            new SetRenderable2DRequest(Child, renderable));

        Assert.True(result.Applied);
        Assert.NotSame(original, result.Document);
        Assert.False(original.Renderables.TryGet(Child, out _));
        Assert.True(result.Document.Renderables.TryGet(Child, out Renderable2DData stored));
        Assert.Equal(renderable, stored);
    }

    [Fact]
    public void WorldDataActuator_SetRenderable_RejectsMissingUnit()
    {
        WorldDataDocument original = WorldDataDocument.FromWorld(CreateWorldDocument());

        WorldActuationResult<WorldDataDocument> result = WorldDataActuator.Apply(
            original,
            new SetRenderable2DRequest(Missing, Renderable("mesh/missing", "material/missing")));

        Assert.Equal(WorldActuationStatus.Rejected, result.Status);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.UnitNotFound, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldDataActuator_SetRenderable_RejectsEmptyMeshRef()
    {
        WorldDataDocument original = WorldDataDocument.FromWorld(CreateWorldDocument());

        WorldActuationResult<WorldDataDocument> result = WorldDataActuator.Apply(
            original,
            new SetRenderable2DRequest(Child, Renderable("   ", "material/child")));

        Assert.Equal(WorldActuationStatus.Rejected, result.Status);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.InvalidMeshRef, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldDataActuator_SetRenderable_RejectsEmptyMaterialRef()
    {
        WorldDataDocument original = WorldDataDocument.FromWorld(CreateWorldDocument());

        WorldActuationResult<WorldDataDocument> result = WorldDataActuator.Apply(
            original,
            new SetRenderable2DRequest(Child, Renderable("mesh/child", "")));

        Assert.Equal(WorldActuationStatus.Rejected, result.Status);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.InvalidMaterialRef, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldDataActuator_RemoveMissingRenderable_ReturnsNoOp()
    {
        WorldDataDocument original = WorldDataDocument.FromWorld(CreateWorldDocument());

        WorldActuationResult<WorldDataDocument> result = WorldDataActuator.Apply(
            original,
            new RemoveRenderable2DRequest(Child));

        Assert.Equal(WorldActuationStatus.NoOp, result.Status);
        Assert.True(result.Success);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.RenderableMissing, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldDataActuator_SetRenderable_DoesNotMutateOriginalDocument()
    {
        WorldDataDocument original = WorldDataDocument.FromWorld(CreateWorldDocument());

        WorldActuationResult<WorldDataDocument> result = WorldDataActuator.Apply(
            original,
            new SetRenderable2DRequest(Child, Renderable("mesh/child", "material/child")));

        Assert.False(original.Renderables.TryGet(Child, out _));
        Assert.True(result.Document.Renderables.TryGet(Child, out _));
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
