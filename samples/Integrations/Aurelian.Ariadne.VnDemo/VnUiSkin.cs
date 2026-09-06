using Dominatus.Assets.Toml;
using Dominatus.SpriteForge;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;

namespace Aurelian.Ariadne.VnDemo;

public sealed class VnUiSkin
{
    public const string AtlasAssetId = "sunkill.ui.atlas";
    private readonly SpriteForgeAtlas atlas;

    private VnUiSkin(SpriteForgeAtlas atlas)
    {
        this.atlas = atlas;
    }

    public SpriteForgeAtlas Atlas => atlas;

    public static VnUiSkin Load(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "Assets", "sunkill-ui.toml");
        SpriteForgeLoadResult result = SpriteForgeTomlLoader.LoadFile(
            path,
            new SpriteForgeLoadOptions { RequireImageFileExists = true });
        if (!result.Success || result.Atlas is null)
        {
            throw new InvalidDataException(AssetDiagnosticFormatter.FormatMany(result.Diagnostics));
        }

        return new VnUiSkin(result.Atlas);
    }

    public MachinaNineSlicePrimitive Create(
        string sourceId,
        string panelId,
        Rect destination,
        ColorToken? tint = null)
    {
        SpriteForgeNineSlicePanel panel = atlas.UiPanels.TryGetValue(panelId, out SpriteForgeNineSlicePanel? found)
            ? found
            : throw new KeyNotFoundException($"SUNKILL UI panel '{panelId}' is not authored in SpriteForge.");
        return new MachinaNineSlicePrimitive(
            sourceId,
            new MachinaTextureAssetId(AtlasAssetId),
            new Rect(panel.X, panel.Y, panel.Width, panel.Height),
            destination,
            new MachinaSliceMargins(panel.Left, panel.Top, panel.Right, panel.Bottom),
            ToMachinaMode(panel.EdgeMode),
            ToMachinaMode(panel.CenterMode),
            panel.BorderScale,
            tint);
    }

    private static MachinaNineSliceMode ToMachinaMode(SpriteForgeTileMode mode)
    {
        return mode switch
        {
            SpriteForgeTileMode.Stretch => MachinaNineSliceMode.Stretch,
            SpriteForgeTileMode.Tile => MachinaNineSliceMode.Tile,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
