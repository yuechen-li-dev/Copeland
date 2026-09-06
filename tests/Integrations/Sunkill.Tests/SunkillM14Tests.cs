using Aurelian.Ariadne.VnDemo;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Machina.Graphics;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;
using Xunit;

namespace Sunkill.Tests;

public sealed class SunkillM14Tests
{
    [Theory]
    [InlineData(2560, 1440, 2.0, 0.0, 0.0)]
    [InlineData(1600, 1200, 1.25, 0.0, 150.0)]
    [InlineData(1200, 600, 0.8333333333333334, 66.66666666666663, 0.0)]
    public void ViewportTransformFitsUniformlyAndRoundtripsPointer(
        int width,
        int height,
        double scale,
        double originX,
        double originY)
    {
        MachinaViewportTransform transform = MachinaViewportTransform.Create(1280, 720, width, height);

        Assert.Equal(scale, transform.Scale, 10);
        Assert.Equal(originX, transform.PhysicalViewport.X, 10);
        Assert.Equal(originY, transform.PhysicalViewport.Y, 10);
        (double physicalX, double physicalY) = transform.ToPhysical(321.25, 456.75);
        (double logicalX, double logicalY) = transform.ToLogical(physicalX, physicalY);
        Assert.Equal(321.25, logicalX, 10);
        Assert.Equal(456.75, logicalY, 10);
    }

    [Theory]
    [InlineData(40, 40, 9)]
    [InlineData(240, 40, 9)]
    [InlineData(40, 240, 9)]
    [InlineData(10, 10, 4)]
    public void StretchedNineSliceCoversExactWideTallAndZeroCenterPanels(
        double width,
        double height,
        int expectedQuadCount)
    {
        var primitive = new MachinaNineSlicePrimitive(
            "test.stretch",
            new MachinaTextureAssetId("test.atlas"),
            new Rect(10, 20, 40, 40),
            new Rect(100, 200, width, height),
            new MachinaSliceMargins(5, 5, 5, 5),
            MachinaNineSliceMode.Stretch,
            MachinaNineSliceMode.Stretch);

        IReadOnlyList<MachinaNineSliceQuad> quads = MachinaNineSliceLowerer.Lower(primitive);

        Assert.Equal(expectedQuadCount, quads.Count);
        Assert.All(quads, quad =>
        {
            Assert.True(quad.DestinationRect.Width > 0);
            Assert.True(quad.DestinationRect.Height > 0);
            Assert.True(quad.SourceRect.Width > 0);
            Assert.True(quad.SourceRect.Height > 0);
        });
        Assert.Equal(100, quads.Min(quad => quad.DestinationRect.X), 10);
        Assert.Equal(200, quads.Min(quad => quad.DestinationRect.Y), 10);
        Assert.Equal(100 + width, quads.Max(quad => quad.DestinationRect.X + quad.DestinationRect.Width), 10);
        Assert.Equal(200 + height, quads.Max(quad => quad.DestinationRect.Y + quad.DestinationRect.Height), 10);
    }

    [Fact]
    public void NineSliceTilesBothAxesAndCropsFinalTilesWithoutGaps()
    {
        var primitive = new MachinaNineSlicePrimitive(
            "test.panel",
            new MachinaTextureAssetId("test.atlas"),
            new Rect(10, 20, 40, 40),
            new Rect(0, 0, 57, 51),
            new MachinaSliceMargins(5, 5, 5, 5),
            MachinaNineSliceMode.Tile,
            MachinaNineSliceMode.Tile,
            tint: ColorToken.White);

        IReadOnlyList<MachinaNineSliceQuad> quads = MachinaNineSliceLowerer.Lower(primitive);

        Assert.True(quads.Count > 9);
        Assert.All(quads, quad =>
        {
            Assert.True(quad.DestinationRect.Width > 0);
            Assert.True(quad.DestinationRect.Height > 0);
            Assert.InRange(quad.SourceRect.X, 10, 50);
            Assert.InRange(quad.SourceRect.Y, 20, 60);
            Assert.True(quad.SourceRect.X + quad.SourceRect.Width <= 50);
            Assert.True(quad.SourceRect.Y + quad.SourceRect.Height <= 60);
        });
        Assert.Contains(quads, quad => quad.SourceRect.Width < 30);
        Assert.Contains(quads, quad => quad.SourceRect.Height < 30);
        Assert.Equal(57, quads.Max(quad => quad.DestinationRect.X + quad.DestinationRect.Width), 10);
        Assert.Equal(51, quads.Max(quad => quad.DestinationRect.Y + quad.DestinationRect.Height), 10);
    }

    [Fact]
    public void InvalidMarginsAndSourceBoundsFailClosed()
    {
        Assert.Throws<ArgumentException>(() => new MachinaNineSlicePrimitive(
            "invalid",
            new MachinaTextureAssetId("atlas"),
            new Rect(0, 0, 16, 16),
            new Rect(0, 0, 100, 100),
            new MachinaSliceMargins(9, 1, 9, 1),
            MachinaNineSliceMode.Stretch,
            MachinaNineSliceMode.Stretch));

        Assert.Throws<ArgumentOutOfRangeException>(() => AurelianNineSliceAdapter.ToInsetUv(
            new Rect(15, 0, 2, 2),
            16,
            16));
    }

    [Fact]
    public void SunkillLoadsSpriteForgeMetadataAndUsesNineSliceOnlyForCards()
    {
        string root = FindRepositoryRoot();
        VnUiSkin skin = VnUiSkin.Load(Path.Combine(
            root,
            "samples",
            "Integrations",
            "Aurelian.Ariadne.VnDemo",
            "Assets",
            "sunkill-ui.toml"));
        using var files = new TestFiles();
        using var app = new RenApp(files.SaveDirectory, files.SettingsPath);
        var layer = new VnMachinaLayer(app, skin);

        MachinaNineSlicePrimitive menuCard = Assert.Single(layer.NineSlices);
        Assert.Equal("skin.menu-shadow", menuCard.SourceId);
        Assert.Equal("sunkill.ui.atlas", menuCard.Texture.Value);

        app.Dispatch(new NewGameIntent());
        MachinaNineSlicePrimitive dialogueCard = Assert.Single(layer.NineSlices);
        Assert.Equal("skin.dialogue-panel", dialogueCard.SourceId);
        Assert.Equal(MachinaNineSliceMode.Stretch, dialogueCard.EdgeMode);
        Assert.Equal(0.5, dialogueCard.BorderScale, 5);
        Assert.DoesNotContain(layer.NineSlices, item => item.SourceId.Contains("button", StringComparison.Ordinal));
    }

    [Fact]
    public void NativeAdapterUsesInsetAtlasUvsAndPhysicalViewport()
    {
        VnUiSkin skin = VnUiSkin.Load(Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Integrations",
            "Aurelian.Ariadne.VnDemo",
            "Assets",
            "sunkill-ui.toml"));
        MachinaNineSlicePrimitive primitive = skin.Create(
            "test.card",
            "dialogue",
            new Rect(44, 470, 1192, 220));
        IReadOnlyList<NativeQuadSubmission> native = AurelianNineSliceAdapter.Lower(
            primitive,
            new Native2DTextureHandle(7),
            skin.Atlas.Width,
            skin.Atlas.Height,
            MachinaViewportTransform.Create(1280, 720, 2560, 1440));

        Assert.NotEmpty(native);
        Assert.All(native, quad =>
        {
            Assert.Equal(new Native2DTextureHandle(7), quad.Texture);
            Assert.InRange(quad.Uv.U0, 0, 1);
            Assert.InRange(quad.Uv.V0, 0, 1);
            Assert.InRange(quad.Uv.U1, 0, 1);
            Assert.InRange(quad.Uv.V1, 0, 1);
        });
        Assert.Equal(88, native.Min(quad => quad.Destination.X), 5);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Copeland.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class TestFiles : IDisposable
    {
        public TestFiles()
        {
            Root = Path.Combine(Path.GetTempPath(), "sunkill-m14-tests", Guid.NewGuid().ToString("N"));
            SaveDirectory = Path.Combine(Root, "saves");
            SettingsPath = Path.Combine(Root, "settings", "settings.json");
        }

        public string Root { get; }
        public string SaveDirectory { get; }
        public string SettingsPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
