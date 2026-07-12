using Aurelian.Actuation.World;
using Aurelian.Actuation.World.Requests;
using Aurelian.World.Units;
using Xunit;

namespace Aurelian.Actuation.Tests;

public sealed class WorldUnitActuatorM0Tests
{
    private static readonly UnitId Root = new(1);
    private static readonly UnitId Animal = new(2);
    private static readonly UnitId Human = new(3);
    private static readonly UnitId Woman = new(4);
    private static readonly UnitId Elder = new(5);
    private static readonly UnitId OldWoman = new(6);
    private static readonly UnitId Room = new(7);
    private static readonly UnitId Button = new(8);
    private static readonly UnitId Card = new(9);
    private static readonly UnitId Missing = new(404);

    [Fact]
    public void WorldUnitActuator_SpawnUnit_AddsDescriptorWithoutMutatingOriginal()
    {
        WorldDocument original = CreateRootDocument();
        WorldUnitDescriptor room = Descriptor(Room, "room");

        WorldActuationResult result = WorldUnitActuator.Apply(original, new SpawnUnitRequest(room));

        Assert.True(result.Applied);
        Assert.True(result.Success);
        Assert.NotSame(original, result.Document);
        Assert.False(original.Units.ContainsKey(Room));
        Assert.True(result.Document.Units.ContainsKey(Room));
    }

    [Fact]
    public void WorldUnitActuator_SpawnDuplicateUnit_ReturnsRejectedDiagnostic()
    {
        WorldDocument original = CreateRootDocument();

        WorldActuationResult result = WorldUnitActuator.Apply(original, new SpawnUnitRequest(Descriptor(Root, "duplicate-root")));

        Assert.Equal(WorldActuationStatus.Rejected, result.Status);
        Assert.Same(original, result.Document);
        WorldActuationDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(WorldActuationDiagnosticCodes.UnitAlreadyExists, diagnostic.Code);
    }

    [Fact]
    public void WorldUnitActuator_AttachChild_AddsImmediateChildWithoutMutatingOriginal()
    {
        WorldDocument original = CreateRootAndRoomDocument();

        WorldActuationResult result = WorldUnitActuator.Apply(original, new AttachChildRequest(Root, new UnitChild(Room, "room")));

        Assert.True(result.Applied);
        Assert.Empty(original.Units[Root].Composition.Children);
        UnitChild child = Assert.Single(result.Document.Units[Root].Composition.Children);
        Assert.Equal(Room, child.UnitId);
        Assert.Equal("room", child.Slot);
    }

    [Fact]
    public void WorldUnitActuator_AttachChild_DoesNotAddTransitiveChildrenToParentDescriptor()
    {
        WorldDocument original = CreateHumanDocumentDetachedFromRoot();

        WorldActuationResult result = WorldUnitActuator.Apply(original, new AttachChildRequest(Root, new UnitChild(OldWoman)));

        Assert.True(result.Applied);
        UnitId[] rootChildren = result.Document.Units[Root].Composition.Children.Select(x => x.UnitId).ToArray();
        Assert.Equal([OldWoman], rootChildren);
        Assert.DoesNotContain(Woman, rootChildren);
        Assert.DoesNotContain(Human, rootChildren);
        Assert.DoesNotContain(Animal, rootChildren);
    }

    [Fact]
    public void WorldUnitActuator_AttachMissingParent_ReturnsRejectedDiagnostic()
    {
        WorldDocument original = CreateRootAndRoomDocument();

        WorldActuationResult result = WorldUnitActuator.Apply(original, new AttachChildRequest(Missing, new UnitChild(Room)));

        Assert.Equal(WorldActuationStatus.Rejected, result.Status);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.ParentNotFound, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldUnitActuator_AttachMissingChild_ReturnsRejectedDiagnostic()
    {
        WorldDocument original = CreateRootDocument();

        WorldActuationResult result = WorldUnitActuator.Apply(original, new AttachChildRequest(Root, new UnitChild(Missing)));

        Assert.Equal(WorldActuationStatus.Rejected, result.Status);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.ChildNotFound, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldUnitActuator_AttachDuplicateChild_ReturnsRejectedDiagnostic()
    {
        WorldDocument original = CreateRootWithRoomDocument();

        WorldActuationResult result = WorldUnitActuator.Apply(original, new AttachChildRequest(Root, new UnitChild(Room, "other")));

        Assert.Equal(WorldActuationStatus.Rejected, result.Status);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.ChildAlreadyAttached, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldUnitActuator_AttachDuplicateSlot_ReturnsRejectedDiagnostic()
    {
        WorldDocument original = new(Root, new Dictionary<UnitId, WorldUnitDescriptor>
        {
            [Root] = Descriptor(Root, "root", new UnitChild(Room, "content")),
            [Room] = Descriptor(Room, "room"),
            [Button] = Descriptor(Button, "button")
        });

        WorldActuationResult result = WorldUnitActuator.Apply(original, new AttachChildRequest(Root, new UnitChild(Button, "content")));

        Assert.Equal(WorldActuationStatus.Rejected, result.Status);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.DuplicateChildSlot, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldUnitActuator_AttachWouldCreateCycle_ReturnsRejectedAndKeepsOriginal()
    {
        WorldDocument original = new(Root, new Dictionary<UnitId, WorldUnitDescriptor>
        {
            [Root] = Descriptor(Root, "root", new UnitChild(Room)),
            [Room] = Descriptor(Room, "room", new UnitChild(Button)),
            [Button] = Descriptor(Button, "button")
        });

        WorldActuationResult result = WorldUnitActuator.Apply(original, new AttachChildRequest(Button, new UnitChild(Root)));

        Assert.Equal(WorldActuationStatus.Rejected, result.Status);
        Assert.Same(original, result.Document);
        Assert.Contains(result.Diagnostics, x => x.Code == WorldActuationDiagnosticCodes.InvalidMutationWouldBreakWorld);
        Assert.Empty(original.Units[Button].Composition.Children);
    }

    [Fact]
    public void WorldUnitActuator_DetachChild_RemovesImmediateChildOnly()
    {
        WorldDocument original = new(Root, new Dictionary<UnitId, WorldUnitDescriptor>
        {
            [Root] = Descriptor(Root, "root", new UnitChild(Room), new UnitChild(Card)),
            [Room] = Descriptor(Room, "room", new UnitChild(Button)),
            [Button] = Descriptor(Button, "button"),
            [Card] = Descriptor(Card, "card")
        });

        WorldActuationResult result = WorldUnitActuator.Apply(original, new DetachChildRequest(Root, Room));

        Assert.True(result.Applied);
        Assert.Equal([Room, Card], original.Units[Root].Composition.Children.Select(x => x.UnitId).ToArray());
        Assert.Equal([Card], result.Document.Units[Root].Composition.Children.Select(x => x.UnitId).ToArray());
        Assert.Equal([Button], result.Document.Units[Room].Composition.Children.Select(x => x.UnitId).ToArray());
    }

    [Fact]
    public void WorldUnitActuator_DetachMissingChild_ReturnsNoOpOrRejectedDiagnostic()
    {
        WorldDocument original = CreateRootWithRoomDocument();

        WorldActuationResult result = WorldUnitActuator.Apply(original, new DetachChildRequest(Root, Button));

        Assert.Equal(WorldActuationStatus.NoOp, result.Status);
        Assert.True(result.Success);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.ChildNotAttached, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldUnitActuator_DestroyRoot_ReturnsRejectedDiagnostic()
    {
        WorldDocument original = CreateRootDocument();

        WorldActuationResult result = WorldUnitActuator.Apply(original, new DestroyUnitRequest(Root));

        Assert.Equal(WorldActuationStatus.Rejected, result.Status);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.CannotDestroyRoot, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldUnitActuator_DestroyLeaf_RemovesUnitAndParentReference()
    {
        WorldDocument original = CreateRootWithRoomDocument();

        WorldActuationResult result = WorldUnitActuator.Apply(original, new DestroyUnitRequest(Room));

        Assert.True(result.Applied);
        Assert.True(original.Units.ContainsKey(Room));
        Assert.Equal([Room], original.Units[Root].Composition.Children.Select(x => x.UnitId).ToArray());
        Assert.False(result.Document.Units.ContainsKey(Room));
        Assert.Empty(result.Document.Units[Root].Composition.Children);
    }

    [Fact]
    public void WorldUnitActuator_DestroyUnitWithChildren_ReturnsRejectedDiagnostic()
    {
        WorldDocument original = new(Root, new Dictionary<UnitId, WorldUnitDescriptor>
        {
            [Root] = Descriptor(Root, "root", new UnitChild(Room)),
            [Room] = Descriptor(Room, "room", new UnitChild(Button)),
            [Button] = Descriptor(Button, "button")
        });

        WorldActuationResult result = WorldUnitActuator.Apply(original, new DestroyUnitRequest(Room));

        Assert.Equal(WorldActuationStatus.Rejected, result.Status);
        Assert.Same(original, result.Document);
        Assert.Equal(WorldActuationDiagnosticCodes.CannotDestroyUnitWithChildren, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldUnitActuator_ReplaceDescriptor_UpdatesKindOrLogicRef()
    {
        WorldDocument original = new(Root, new Dictionary<UnitId, WorldUnitDescriptor>
        {
            [Root] = Descriptor(Root, "root", new UnitChild(Room)),
            [Room] = new WorldUnitDescriptor(Room, new UnitKindId("room"), UnitComposition.Empty, new UnitLogicRef("logic://old"))
        });
        WorldUnitDescriptor replacement = new(Room, new UnitKindId("special-room"), UnitComposition.Empty, new UnitLogicRef("logic://new"));

        WorldActuationResult result = WorldUnitActuator.Apply(original, new ReplaceUnitDescriptorRequest(replacement));

        Assert.True(result.Applied);
        Assert.Equal("room", original.Units[Room].Kind.Value);
        Assert.Equal("logic://old", original.Units[Room].Logic?.Value);
        Assert.Equal("special-room", result.Document.Units[Room].Kind.Value);
        Assert.Equal("logic://new", result.Document.Units[Room].Logic?.Value);
        Assert.True(WorldUnitResolver.Resolve(result.Document).Success);
    }

    private static WorldDocument CreateRootDocument() => new(Root, new Dictionary<UnitId, WorldUnitDescriptor>
    {
        [Root] = Descriptor(Root, "root")
    });

    private static WorldDocument CreateRootAndRoomDocument() => new(Root, new Dictionary<UnitId, WorldUnitDescriptor>
    {
        [Root] = Descriptor(Root, "root"),
        [Room] = Descriptor(Room, "room")
    });

    private static WorldDocument CreateRootWithRoomDocument() => new(Root, new Dictionary<UnitId, WorldUnitDescriptor>
    {
        [Root] = Descriptor(Root, "root", new UnitChild(Room)),
        [Room] = Descriptor(Room, "room")
    });

    private static WorldDocument CreateHumanDocumentDetachedFromRoot() => new(Root, new Dictionary<UnitId, WorldUnitDescriptor>
    {
        [Root] = Descriptor(Root, "root"),
        [OldWoman] = Descriptor(OldWoman, "old-woman", new UnitChild(Woman), new UnitChild(Elder)),
        [Woman] = Descriptor(Woman, "woman", new UnitChild(Human)),
        [Human] = Descriptor(Human, "human", new UnitChild(Animal)),
        [Animal] = Descriptor(Animal, "animal"),
        [Elder] = Descriptor(Elder, "elder")
    });

    private static WorldUnitDescriptor Descriptor(UnitId id, string kind, params UnitChild[] children) =>
        new(id, new UnitKindId(kind), children.Length == 0 ? UnitComposition.Empty : new UnitComposition(children));
}
