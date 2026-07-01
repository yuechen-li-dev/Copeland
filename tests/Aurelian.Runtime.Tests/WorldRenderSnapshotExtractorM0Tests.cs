using Aurelian.Rendering.Contracts;
using Aurelian.Rendering.Contracts.CommandPlans;
using Aurelian.Rendering.Contracts.Snapshots;
using Aurelian.Rendering.Null;
using Aurelian.Runtime.Rendering;
using Aurelian.World.Stores;
using Aurelian.World.Units;
using Xunit;

namespace Aurelian.Runtime.Tests;

public sealed class WorldRenderSnapshotExtractorM0Tests
{
    private static readonly UnitId Root = new(1);
    private static readonly UnitId Child = new(2);
    private static readonly RenderPipelineRef Pipeline = new("pipeline/main2d");
    private static readonly RenderShaderRef Shader = new("shader/unlit2d");
    private static readonly RenderTargetRef Target = new("target/null");

    [Fact]
    public void WorldRenderSnapshotExtractor_ExtractsRenderableUnitsIntoSnapshot()
    {
        Transform2 transform = new(10, 20, 0.25, 2, 3);
        WorldDataDocument document = WorldDataDocument.FromWorld(CreateWorldDocument())
            .WithTransforms(Transform2Store.Empty.Set(Child, transform))
            .WithRenderables(Renderable2DStore.Empty.Set(Child, Renderable("mesh/child", "material/child", sortOrder: 5)));

        RenderSnapshotResult result = WorldRenderSnapshotExtractor.Extract(document, Options(7));

        Assert.Equal(RenderSnapshotStatus.Ready, result.Status);
        Assert.True(result.Success);
        Assert.Equal(new RenderFrameId(7), result.Snapshot.FrameId);
        Assert.Equal("MainCamera", Assert.Single(result.Snapshot.Cameras).Id);
        RenderItem2D item = Assert.Single(result.Snapshot.Items);
        Assert.Equal(Child.ToString(), item.Id);
        Assert.Equal("mesh/child", item.Mesh.Value);
        Assert.Equal("material/child", item.Material.Value);
        Assert.Equal(5, item.SortOrder);
        Assert.Equal(new RenderTransform2(10, 20, 0.25, 2, 3), item.Transform);
    }

    [Fact]
    public void WorldRenderSnapshotExtractor_SkipsInvisibleRenderables()
    {
        WorldDataDocument document = WorldDataDocument.FromWorld(CreateWorldDocument())
            .WithRenderables(Renderable2DStore.Empty.Set(Child, Renderable("mesh/child", "material/child", visible: false)));

        RenderSnapshotResult result = WorldRenderSnapshotExtractor.Extract(document, Options());

        Assert.Equal(RenderSnapshotStatus.Empty, result.Status);
        Assert.Empty(result.Snapshot.Items);
        Assert.Equal(WorldRenderSnapshotDiagnosticCodes.NoRenderableUnits, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldRenderSnapshotExtractor_UsesIdentityTransformWhenMissing()
    {
        WorldDataDocument document = WorldDataDocument.FromWorld(CreateWorldDocument())
            .WithRenderables(Renderable2DStore.Empty.Set(Child, Renderable("mesh/child", "material/child")));

        RenderSnapshotResult result = WorldRenderSnapshotExtractor.Extract(document, Options());

        RenderItem2D item = Assert.Single(result.Snapshot.Items);
        Assert.Equal(RenderTransform2.Identity, item.Transform);
    }

    [Fact]
    public void WorldRenderSnapshotExtractor_ProducesEmptySnapshotWhenNoRenderableUnits()
    {
        WorldDataDocument document = WorldDataDocument.FromWorld(CreateWorldDocument());

        RenderSnapshotResult result = WorldRenderSnapshotExtractor.Extract(document, Options());

        Assert.Equal(RenderSnapshotStatus.Empty, result.Status);
        Assert.True(result.Success);
        Assert.Single(result.Snapshot.Cameras);
        Assert.Empty(result.Snapshot.Items);
        Assert.Equal(WorldRenderSnapshotDiagnosticCodes.NoRenderableUnits, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldRenderSnapshotExtractor_RejectsInvalidWorldDocument()
    {
        WorldDocument invalidWorld = new(Root, new Dictionary<UnitId, WorldUnitDescriptor>());
        WorldDataDocument document = WorldDataDocument.FromWorld(invalidWorld);

        RenderSnapshotResult result = WorldRenderSnapshotExtractor.Extract(document, Options());

        Assert.Equal(RenderSnapshotStatus.Rejected, result.Status);
        Assert.False(result.Success);
        Assert.Empty(result.Snapshot.Cameras);
        Assert.Empty(result.Snapshot.Items);
        Assert.Equal(WorldRenderSnapshotDiagnosticCodes.WorldResolutionFailed, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void WorldToRenderToCommandPlanToNullRenderer_ProducesDeterministicTrace()
    {
        WorldDataDocument document = WorldDataDocument.FromWorld(CreateWorldDocument())
            .WithTransforms(Transform2Store.Empty.Set(Child, new Transform2(12.5, -4, 0, 1, 1)))
            .WithRenderables(Renderable2DStore.Empty.Set(Child, Renderable("mesh/quad", "material/unlit", sortOrder: 4)));

        RenderSnapshotResult snapshotResult = WorldRenderSnapshotExtractor.Extract(document, Options(42));
        RenderCommandPlan plan = RenderCommandPlanBuilder.FromSnapshot(snapshotResult.Snapshot, Pipeline, Shader, Target);
        NullRenderResult renderResult = new NullRenderer().Render(plan);

        Assert.Equal(RenderSnapshotStatus.Ready, snapshotResult.Status);
        Assert.Equal(RenderCommandPlanStatus.Ready, plan.Status);
        Assert.Equal(NullRenderStatus.Rendered, renderResult.Status);
        Assert.Equal(1, renderResult.Trace.DrawCount);
        NullRenderPassTrace pass = Assert.Single(renderResult.Trace.Passes);
        Assert.Equal("Main2D", pass.Name);
        NullRenderDrawTrace draw = Assert.Single(pass.Draws);
        Assert.Equal(Child.ToString(), draw.Id);
        Assert.Equal("mesh/quad", draw.Mesh);
        Assert.Equal("material/unlit", draw.Material);
        Assert.Equal(12.5, draw.X);
        Assert.Equal(-4, draw.Y);
        Assert.Equal(4, draw.SortOrder);
    }

    private static WorldRenderSnapshotOptions Options(ulong frame = 1) => new(new RenderFrameId(frame));

    private static Renderable2DData Renderable(
        string mesh,
        string material,
        bool visible = true,
        int sortOrder = 0) =>
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
