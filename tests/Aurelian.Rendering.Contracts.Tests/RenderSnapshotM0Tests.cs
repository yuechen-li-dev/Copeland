using System;
using System.IO;
using Aurelian.Rendering.Contracts;
using Aurelian.Rendering.Contracts.Snapshots;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class RenderSnapshotM0Tests
{
    [Fact]
    public void RenderTransform2_Identity_HasExpectedValues()
    {
        Assert.Equal(new RenderTransform2(0, 0, 0, 1, 1), RenderTransform2.Identity);
    }

    [Fact]
    public void RenderResourceRefs_ToString_ReturnsValue()
    {
        Assert.Equal("mesh/player", new RenderMeshRef("mesh/player").ToString());
        Assert.Equal("material/sprite", new RenderMaterialRef("material/sprite").ToString());
        Assert.Equal("texture/diffuse", new RenderTextureRef("texture/diffuse").ToString());
    }

    [Fact]
    public void RenderSnapshot_EmptySnapshot_IsEmpty()
    {
        RenderSnapshot snapshot = new(new RenderFrameId(1), [], []);

        Assert.True(snapshot.IsEmpty);
    }

    [Fact]
    public void RenderSnapshot_CanHoldCameraAndItems()
    {
        RenderCamera2D camera = new("main", RenderTransform2.Identity, 1920, 1080);
        RenderItem2D item = new(
            "unit-1",
            new RenderTransform2(10, 20, 0.5, 2, 3),
            new RenderMeshRef("mesh/quad"),
            new RenderMaterialRef("material/unlit"),
            SortOrder: 7);

        RenderSnapshot snapshot = new(new RenderFrameId(2), [camera], [item]);

        Assert.False(snapshot.IsEmpty);
        Assert.Equal(camera, Assert.Single(snapshot.Cameras));
        Assert.Equal(item, Assert.Single(snapshot.Items));
    }

    [Fact]
    public void RenderSnapshotResult_ReadyWithoutErrors_IsSuccess()
    {
        RenderSnapshot snapshot = new(new RenderFrameId(3), [], []);
        RenderSnapshotResult result = new(
            RenderSnapshotStatus.Ready,
            snapshot,
            [new RenderSnapshotDiagnostic(
                RenderSnapshotDiagnosticCodes.MissingRenderable,
                RenderSnapshotDiagnosticSeverity.Info,
                "No renderable items were emitted.")]);

        Assert.True(result.Success);
    }

    [Fact]
    public void RenderSnapshotResult_RejectedWithError_IsNotSuccess()
    {
        RenderSnapshot snapshot = new(new RenderFrameId(4), [], []);
        RenderSnapshotResult result = new(
            RenderSnapshotStatus.Rejected,
            snapshot,
            [new RenderSnapshotDiagnostic(
                RenderSnapshotDiagnosticCodes.InvalidRenderItem,
                RenderSnapshotDiagnosticSeverity.Error,
                "Render item is invalid.")]);

        Assert.False(result.Success);
    }

    [Fact]
    public void RenderContracts_DoNotRequireWorldTypes()
    {
        string projectFile = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/Aurelian.Rendering.Contracts/Aurelian.Rendering.Contracts.csproj"));
        string projectText = File.ReadAllText(projectFile);
        string forbiddenReference = "Aurelian." + "World";

        Assert.DoesNotContain(forbiddenReference, projectText, StringComparison.Ordinal);
    }
}
