using Aurelian.Actuation.World;
using Aurelian.Actuation.World.Requests;
using Aurelian.World.Stores;
using Aurelian.World.Units;
using Xunit;

namespace Aurelian.Actuation.Tests;

public sealed class WorldDataActuatorM0Tests
{
    private static readonly UnitId Root = new(1);
    private static readonly UnitId Child = new(2);
    private static readonly UnitId Missing = new(999);

    [Fact]
    public void WorldDataActuator_SetUnitName_AppliesToExistingUnit()
    {
        WorldDataDocument original = WorldDataDocument.FromWorld(CreateWorldDocument());

        WorldActuationResult<WorldDataDocument> result = WorldDataActuator.Apply(
            original,
            new SetUnitNameRequest(Child, new UnitName("Child")));

        Assert.True(result.Applied);
        Assert.NotSame(original, result.Document);
        Assert.False(original.Names.TryGet(Child, out _));
        Assert.True(result.Document.Names.TryGet(Child, out UnitName? name));
        Assert.Equal("Child", name.Value);
    }

    [Fact]
    public void WorldDataActuator_SetUnitName_RejectsMissingUnit()
    {
        WorldDataDocument original = WorldDataDocument.FromWorld(CreateWorldDocument());

        WorldActuationResult<WorldDataDocument> result = WorldDataActuator.Apply(
            original,
            new SetUnitNameRequest(Missing, new UnitName("Missing")));

        Assert.Equal(WorldActuationStatus.Rejected, result.Status);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.UnitNotFound, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldDataActuator_SetUnitName_RejectsEmptyName()
    {
        WorldDataDocument original = WorldDataDocument.FromWorld(CreateWorldDocument());

        WorldActuationResult<WorldDataDocument> result = WorldDataActuator.Apply(
            original,
            new SetUnitNameRequest(Child, new UnitName("   ")));

        Assert.Equal(WorldActuationStatus.Rejected, result.Status);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.InvalidUnitName, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldDataActuator_RemoveMissingName_ReturnsNoOp()
    {
        WorldDataDocument original = WorldDataDocument.FromWorld(CreateWorldDocument());

        WorldActuationResult<WorldDataDocument> result = WorldDataActuator.Apply(
            original,
            new RemoveUnitNameRequest(Child));

        Assert.Equal(WorldActuationStatus.NoOp, result.Status);
        Assert.True(result.Success);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.UnitNameNotSet, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldDataActuator_SetTransform_AppliesToExistingUnit()
    {
        WorldDataDocument original = WorldDataDocument.FromWorld(CreateWorldDocument());
        Transform2 transform = new(3, 4, 0.5, 1, 2);

        WorldActuationResult<WorldDataDocument> result = WorldDataActuator.Apply(
            original,
            new SetUnitTransform2Request(Child, transform));

        Assert.True(result.Applied);
        Assert.False(original.Transforms.TryGet(Child, out _));
        Assert.True(result.Document.Transforms.TryGet(Child, out Transform2 stored));
        Assert.Equal(transform, stored);
    }

    [Fact]
    public void WorldDataActuator_SetTransform_RejectsMissingUnit()
    {
        WorldDataDocument original = WorldDataDocument.FromWorld(CreateWorldDocument());

        WorldActuationResult<WorldDataDocument> result = WorldDataActuator.Apply(
            original,
            new SetUnitTransform2Request(Missing, Transform2.Identity));

        Assert.Equal(WorldActuationStatus.Rejected, result.Status);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.UnitNotFound, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldDataActuator_SetTransform_RejectsNonFiniteValues()
    {
        WorldDataDocument original = WorldDataDocument.FromWorld(CreateWorldDocument());
        Transform2 invalid = new(double.NaN, 0, 0, 1, 1);

        WorldActuationResult<WorldDataDocument> result = WorldDataActuator.Apply(
            original,
            new SetUnitTransform2Request(Child, invalid));

        Assert.Equal(WorldActuationStatus.Rejected, result.Status);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.InvalidTransform, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldDataActuator_RemoveMissingTransform_ReturnsNoOp()
    {
        WorldDataDocument original = WorldDataDocument.FromWorld(CreateWorldDocument());

        WorldActuationResult<WorldDataDocument> result = WorldDataActuator.Apply(
            original,
            new RemoveUnitTransform2Request(Child));

        Assert.Equal(WorldActuationStatus.NoOp, result.Status);
        Assert.True(result.Success);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.UnitTransformNotSet, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldDataActuator_DoesNotMutateOriginalDocument()
    {
        WorldDataDocument original = WorldDataDocument.FromWorld(CreateWorldDocument());
        WorldActuationResult<WorldDataDocument> named = WorldDataActuator.Apply(
            original,
            new SetUnitNameRequest(Root, new UnitName("Root")));

        WorldActuationResult<WorldDataDocument> transformed = WorldDataActuator.Apply(
            named.Document,
            new SetUnitTransform2Request(Root, new Transform2(9, 8, 0, 1, 1)));

        Assert.False(original.Names.TryGet(Root, out _));
        Assert.False(original.Transforms.TryGet(Root, out _));
        Assert.True(named.Document.Names.TryGet(Root, out _));
        Assert.False(named.Document.Transforms.TryGet(Root, out _));
        Assert.True(transformed.Document.Names.TryGet(Root, out _));
        Assert.True(transformed.Document.Transforms.TryGet(Root, out _));
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
