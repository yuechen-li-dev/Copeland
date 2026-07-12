using Aurelian.World.Units;
using Xunit;

namespace Aurelian.World.Tests;

public sealed class WorldUnitM0Tests
{
    private static readonly UnitId Animal = new(1);
    private static readonly UnitId Human = new(2);
    private static readonly UnitId Woman = new(3);
    private static readonly UnitId Elder = new(4);
    private static readonly UnitId OldWoman = new(5);
    private static readonly UnitId Village = new(6);
    private static readonly UnitId Room = new(7);
    private static readonly UnitId Chair = new(8);

    [Fact]
    public void WorldUnitDescriptor_CompositionDeclaresImmediateChildrenOnly()
    {
        WorldDocument document = CreateHumanDocument();

        WorldUnitDescriptor descriptor = document.Units[OldWoman];
        UnitId[] immediateChildren = descriptor.Composition.Children.Select(x => x.UnitId).ToArray();

        Assert.Equal([Woman, Elder], immediateChildren);
        Assert.DoesNotContain(Human, immediateChildren);
        Assert.DoesNotContain(Animal, immediateChildren);
    }

    [Fact]
    public void WorldUnitResolver_ResolvesRootAndImmediateChildren()
    {
        WorldResolutionResult result = WorldUnitResolver.Resolve(CreateHumanDocument());

        Assert.True(result.Success);
        Assert.NotNull(result.World);
        Assert.Equal(OldWoman, result.World.RootId);
        Assert.Equal([Woman, Elder], result.World.GetImmediateChildren(OldWoman));
        Assert.True(result.World.TryGetParent(Woman, out UnitId parent));
        Assert.Equal(OldWoman, parent);
    }

    [Fact]
    public void WorldUnitResolver_ComputesTransitiveDescendants()
    {
        WorldResolutionResult result = WorldUnitResolver.Resolve(CreateHumanDocument());

        Assert.True(result.Success);
        Assert.NotNull(result.World);
        Assert.Equal([Woman, Human, Animal, Elder], result.World.GetTransitiveDescendants(OldWoman));
    }

    [Fact]
    public void WorldUnitResolver_PreOrderTraversalIsDeterministic()
    {
        WorldDocument document = CreateHumanDocument();

        WorldResolutionResult first = WorldUnitResolver.Resolve(document);
        WorldResolutionResult second = WorldUnitResolver.Resolve(document);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotNull(first.World);
        Assert.NotNull(second.World);
        Assert.Equal([OldWoman, Woman, Human, Animal, Elder], first.World.PreOrder);
        Assert.Equal(first.World.PreOrder, second.World.PreOrder);
    }

    [Fact]
    public void WorldUnitResolver_MissingChild_ReturnsDiagnostic()
    {
        UnitId missing = new(404);
        WorldDocument document = new(OldWoman, new Dictionary<UnitId, WorldUnitDescriptor>
        {
            [OldWoman] = Descriptor(OldWoman, "old-woman", missing)
        });

        WorldResolutionResult result = WorldUnitResolver.Resolve(document);

        Assert.False(result.Success);
        WorldResolutionDiagnostic diagnostic = Assert.Single(result.Diagnostics, x => x.Code == WorldResolutionDiagnosticCodes.ChildUnitMissing);
        Assert.Equal(WorldResolutionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(OldWoman, diagnostic.UnitId);
        Assert.Equal(missing, diagnostic.RelatedUnitId);
    }

    [Fact]
    public void WorldUnitResolver_DuplicateImmediateChild_ReturnsDiagnostic()
    {
        WorldDocument document = new(OldWoman, new Dictionary<UnitId, WorldUnitDescriptor>
        {
            [OldWoman] = new WorldUnitDescriptor(
                OldWoman,
                new UnitKindId("old-woman"),
                new UnitComposition([new UnitChild(Woman), new UnitChild(Woman)])),
            [Woman] = Descriptor(Woman, "woman")
        });

        WorldResolutionResult result = WorldUnitResolver.Resolve(document);

        Assert.False(result.Success);
        WorldResolutionDiagnostic diagnostic = Assert.Single(result.Diagnostics, x => x.Code == WorldResolutionDiagnosticCodes.DuplicateImmediateChild);
        Assert.Equal(WorldResolutionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(OldWoman, diagnostic.UnitId);
        Assert.Equal(Woman, diagnostic.RelatedUnitId);
    }

    [Fact]
    public void WorldUnitResolver_DuplicateChildSlot_ReturnsDiagnostic()
    {
        WorldDocument document = new(OldWoman, new Dictionary<UnitId, WorldUnitDescriptor>
        {
            [OldWoman] = new WorldUnitDescriptor(
                OldWoman,
                new UnitKindId("old-woman"),
                new UnitComposition([new UnitChild(Woman, "base"), new UnitChild(Elder, "base")])),
            [Woman] = Descriptor(Woman, "woman"),
            [Elder] = Descriptor(Elder, "elder")
        });

        WorldResolutionResult result = WorldUnitResolver.Resolve(document);

        Assert.False(result.Success);
        WorldResolutionDiagnostic diagnostic = Assert.Single(result.Diagnostics, x => x.Code == WorldResolutionDiagnosticCodes.DuplicateChildSlot);
        Assert.Equal(WorldResolutionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(OldWoman, diagnostic.UnitId);
        Assert.Equal(Elder, diagnostic.RelatedUnitId);
    }

    [Fact]
    public void WorldUnitResolver_Cycle_ReturnsDiagnostic()
    {
        WorldDocument document = new(OldWoman, new Dictionary<UnitId, WorldUnitDescriptor>
        {
            [OldWoman] = Descriptor(OldWoman, "old-woman", Woman),
            [Woman] = Descriptor(Woman, "woman", Human),
            [Human] = Descriptor(Human, "human", OldWoman)
        });

        WorldResolutionResult result = WorldUnitResolver.Resolve(document);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, x => x.Code == WorldResolutionDiagnosticCodes.CompositionCycle && x.Severity == WorldResolutionDiagnosticSeverity.Error);
    }

    [Fact]
    public void WorldUnitResolver_LogicRefIsOpaqueAndDoesNotExecute()
    {
        UnitLogicRef logic = new("logic.old-woman.idle");
        WorldDocument document = new(OldWoman, new Dictionary<UnitId, WorldUnitDescriptor>
        {
            [OldWoman] = Descriptor(OldWoman, "old-woman", logic: logic)
        });

        WorldResolutionResult result = WorldUnitResolver.Resolve(document);

        Assert.True(result.Success);
        Assert.NotNull(result.World);
        ResolvedWorldUnit unit = result.World.Units[OldWoman];
        Assert.Equal(logic, unit.Logic);
        Assert.Equal("logic.old-woman.idle", unit.Logic?.ToString());
    }

    [Fact]
    public void WorldDocument_DoesNotRequireRendererAssetsOrDominatus()
    {
        WorldDocument document = new(Village, new Dictionary<UnitId, WorldUnitDescriptor>
        {
            [Village] = Descriptor(Village, "village", Room),
            [Room] = Descriptor(Room, "room", Chair),
            [Chair] = Descriptor(Chair, "chair")
        });

        WorldResolutionResult result = WorldUnitResolver.Resolve(document);

        Assert.True(result.Success);
        Assert.NotNull(result.World);
        Assert.Equal([Room], result.World.GetImmediateChildren(Village));
        Assert.Equal([Room, Chair], result.World.GetTransitiveDescendants(Village));
    }

    private static WorldDocument CreateHumanDocument() => new(OldWoman, new Dictionary<UnitId, WorldUnitDescriptor>
    {
        [OldWoman] = Descriptor(OldWoman, "old-woman", Woman, Elder),
        [Woman] = Descriptor(Woman, "woman", Human),
        [Human] = Descriptor(Human, "human", Animal),
        [Animal] = Descriptor(Animal, "animal"),
        [Elder] = Descriptor(Elder, "elder")
    });

    private static WorldUnitDescriptor Descriptor(UnitId id, string kind, params UnitId[] children) =>
        new(id, new UnitKindId(kind), new UnitComposition(children.Select(x => new UnitChild(x)).ToArray()));

    private static WorldUnitDescriptor Descriptor(UnitId id, string kind, UnitLogicRef logic) =>
        new(id, new UnitKindId(kind), UnitComposition.Empty, logic);
}
