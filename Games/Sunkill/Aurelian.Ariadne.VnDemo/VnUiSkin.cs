using Dominatus.Assets.Toml;
using Dominatus.SpriteForge;
using Copeland.SpanAllocation;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Presentation;

namespace Aurelian.Ariadne.VnDemo;

public sealed class VnUiSkin
{
    public const string AtlasAssetId = "sunkill.ui.atlas";
    private readonly SpriteForgeAtlas atlas;
    private readonly IReadOnlyDictionary<string, SpriteForgeNineSlicePanel> nineSlicePanels;

    private VnUiSkin(SpriteForgeAtlas atlas, SpriteForgeAtlas? legacyAtlas)
    {
        this.atlas = atlas;
        nineSlicePanels = MergeNineSlicePanels(atlas, legacyAtlas);
    }

    public SpriteForgeAtlas Atlas => atlas;

    public IReadOnlyDictionary<string, SpriteForgeNineSlicePanel> NineSlicePanels => nineSlicePanels;

    public static VnUiSkin Load(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "Assets", "sunkill-dialogue-panel.runtime.toml");
        SpriteForgeAtlas atlas = LoadAtlas(path);
        SpriteForgeAtlas? legacyAtlas = null;
        if (atlas.AuthoringKind == SpriteForgeAssetAuthoringKind.RuntimeToml)
        {
            string legacyPath = Path.Combine(Path.GetDirectoryName(path)!, "sunkill-ui.toml");
            if (File.Exists(legacyPath))
            {
                legacyAtlas = LoadAtlas(legacyPath);
            }
        }

        return new VnUiSkin(atlas, legacyAtlas);
    }

    public MachinaProgrammablePanelPrimitive CreateProgrammable(
        string sourceId,
        string panelId,
        Rect destination,
        ColorToken? tint = null)
    {
        SpriteForgeProgrammablePanel panel = atlas.ProgrammablePanels.TryGetValue(panelId, out SpriteForgeProgrammablePanel? found)
            ? found
            : throw new KeyNotFoundException($"SUNKILL programmable UI panel '{panelId}' is not authored in SpriteForge.");
        return new MachinaProgrammablePanelPrimitive(
            sourceId,
            new MachinaTextureAssetId(AtlasAssetId),
            destination,
            Region(panel.TopLeftRegionId),
            Region(panel.TopRightRegionId),
            Region(panel.BottomRightRegionId),
            Region(panel.BottomLeftRegionId),
            Edge(panel.Top),
            Edge(panel.Right),
            Edge(panel.Bottom),
            Edge(panel.Left),
            ToMachinaCenterPolicy(panel.CenterPolicy),
            panel.CenterRegionId is null ? null : Region(panel.CenterRegionId),
            panel.BorderScale,
            tint);
    }

    private MachinaPanelEdgeProgram Edge(SpriteForgeEdgeProgram edge)
    {
        return new MachinaPanelEdgeProgram(edge.Segments.Select(segment => new MachinaPanelEdgeSegment(
            segment.Id,
            Region(segment.RegionId),
            segment.Allocation == SpriteForgeAllocationKind.Fixed
                ? SpanAllocationKind.Fixed
                : SpanAllocationKind.Flex,
            segment.MinimumLength,
            segment.Weight,
            segment.Sampling switch
            {
                SpriteForgeSamplingMode.Stretch => MachinaPanelSampling.Stretch,
                SpriteForgeSamplingMode.Tile => MachinaPanelSampling.Tile,
                SpriteForgeSamplingMode.Crop => MachinaPanelSampling.Crop,
                _ => throw new ArgumentOutOfRangeException(nameof(segment.Sampling)),
            })).ToArray());
    }

    private Rect Region(string id)
    {
        SpriteForgeRegion region = atlas.Regions.TryGetValue(id, out SpriteForgeRegion? found)
            ? found
            : throw new KeyNotFoundException($"SUNKILL UI region '{id}' is not authored in SpriteForge.");
        return new Rect(region.X, region.Y, region.Width, region.Height);
    }

    private static MachinaPanelCenterPolicy ToMachinaCenterPolicy(SpriteForgeCenterPolicy policy)
    {
        return policy switch
        {
            SpriteForgeCenterPolicy.AnalyticFill => MachinaPanelCenterPolicy.AnalyticFill,
            SpriteForgeCenterPolicy.StretchRegion => MachinaPanelCenterPolicy.StretchRegion,
            SpriteForgeCenterPolicy.TileRegion => MachinaPanelCenterPolicy.TileRegion,
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, null),
        };
    }

    public MachinaNineSlicePrimitive Create(
        string sourceId,
        string panelId,
        Rect destination,
        ColorToken? tint = null)
    {
        if (!nineSlicePanels.TryGetValue(panelId, out SpriteForgeNineSlicePanel? panel))
        {
            throw new KeyNotFoundException($"SUNKILL UI panel '{panelId}' is not authored in SpriteForge.");
        }

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

    private static SpriteForgeAtlas LoadAtlas(string path)
    {
        SpriteForgeLoadResult result = SpriteForgeTomlLoader.LoadFile(
            path,
            new SpriteForgeLoadOptions { RequireImageFileExists = true });
        if (!result.Success || result.Atlas is null)
        {
            throw new InvalidDataException(AssetDiagnosticFormatter.FormatMany(result.Diagnostics));
        }

        return result.Atlas;
    }

    private static IReadOnlyDictionary<string, SpriteForgeNineSlicePanel> MergeNineSlicePanels(
        SpriteForgeAtlas primary,
        SpriteForgeAtlas? legacy)
    {
        var merged = new Dictionary<string, SpriteForgeNineSlicePanel>(StringComparer.Ordinal);
        if (legacy is not null)
        {
            foreach ((string id, SpriteForgeNineSlicePanel panel) in legacy.UiPanels)
            {
                merged.Add(id, panel);
            }
        }

        foreach ((string id, SpriteForgeNineSlicePanel panel) in primary.UiPanels)
        {
            merged[id] = panel;
        }

        return merged;
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
