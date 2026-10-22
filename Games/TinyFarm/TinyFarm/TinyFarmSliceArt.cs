using Aurelian.GameWorld2D;
using Aurelian.Spatial2D;
using TinyFarm.Core;

namespace TinyFarm.Native;

internal sealed class TinyFarmSliceArt
{
    internal static IReadOnlyList<GroundBrushStroke> FarmPathStrokes { get; } = Strokes(TinyFarmLandscape.FarmRoads);
    internal static IReadOnlyList<GroundBrushStroke> RiversidePathStrokes { get; } = Strokes(TinyFarmLandscape.RiversideRoads);

    private static GroundBrushStroke[] Strokes(IReadOnlyList<WorldPath> paths)
    {
        return paths.Select(path => new GroundBrushStroke(
            path.Centerline.Select(point => new WorldPoint2(point.X, point.Y)).ToArray(), path.Width / 2)).ToArray();
    }

    public TinyFarmSliceArt()
    {
        ChestPoses = GridSpriteSheet.Import(Chest, 2, 1, 64);
        Title = TinyFarmM24Assets.LoadResource("tinyfarm-title-sleeping-spring",
            Path.Combine(AppContext.BaseDirectory, "Assets/GateA/title-sleeping-spring.png"),
            "78b1ae5809dd2873d96eb0e34fa7dccd01b5ec7c9b1a88e8abb6d44c8a63bf34",
            SpriteSampling.Linear);
        Gardener = TinyFarmM24Assets.LoadResource("tinyfarm-gardener-gate-a",
            Path.Combine(AppContext.BaseDirectory, "Assets/GateA/gardener.png"),
            "e37877db50de173e9287527fcd0ae055b52bb4122aa8745ab7a48f62e6165b0d",
            SpriteSampling.Linear);
        Poses = GridSpriteSheet.Import(Gardener, 4, 4, 64);
        Slime = TinyFarmM24Assets.LoadResource("tinyfarm-slime-gate-a",
            Path.Combine(AppContext.BaseDirectory, "Assets/GateA/slime.png"),
            "4186270097e953cabe582ffe3f2bb5ed041d746382f2e0c071dd986580100138",
            SpriteSampling.Linear);
        SlimePoses = GridSpriteSheet.Import(Slime, 4, 1, 50);
        Mara = TinyFarmM24Assets.LoadResource("tinyfarm-mara-walk-v1",
            Path.Combine(AppContext.BaseDirectory, "Assets/characters/mara-v1/walk-sheet.png"),
            "5c73b1bd16fb25464d70360db42ea8bcbc5eef7df45e60109523d6f2cd8b7f8b",
            SpriteSampling.Linear);
        MaraPoses = GridSpriteSheet.Import(Mara, 4, 4, 64);
        Turnip = TinyFarmM24Assets.LoadResource("tinyfarm-turnip-gate-a",
            Path.Combine(AppContext.BaseDirectory, "Assets/GateA/turnip.png"),
            "93cd63902d684125c3b77979b2d35b8a29482a0687263c94ecb713915c775b2e",
            SpriteSampling.Linear);
        TurnipPose = GridSpriteSheet.Import(Turnip, 1, 1, 32)[0];
        Floors = TinyFarmM24Assets.LoadResource("tinyfarm-floors-gate-a",
            Path.Combine(AppContext.BaseDirectory, "Assets/GateA/floors.png"),
            "2b03eeb10682918c1cd67f1560b9290881c1f178adae40f2f2ca1ad5cafbbd0c",
            SpriteSampling.Linear);
        Props = TinyFarmM24Assets.LoadResource("tinyfarm-props-gate-a",
            Path.Combine(AppContext.BaseDirectory, "Assets/GateA/props.png"),
            "1b0bcb6fc6cd8ac422ec199c392e5c77fe78fcb0c20ad6332edb985189ff682d",
            SpriteSampling.Linear);
        IReadOnlyList<SpriteFrameMetadata> imported = GridSpriteSheet.Import(Props, 4, 1, 48);
        double[] heights = [72, 52, 48, 44];
        PropPoses = imported.Select((pose, index) => pose with { Scale = heights[index] / pose.Height }).ToArray();
        FarmPath = GroundBrushRasterizer.Realize(new SpriteAssetId("opening-farm-path"), 18, 12, 64,
            FarmPathStrokes, 0xB9A67BC8);
        Bank = GroundBrushRasterizer.Realize(new SpriteAssetId("opening-river-bank"), 22, 14, 64,
            [new GroundBrushStroke([new(15, 0), new(15, 14)], 1.12)], 0xA5996EEF);
        River = GroundBrushRasterizer.Realize(new SpriteAssetId("opening-river-water"), 22, 14, 64,
            [new GroundBrushStroke([new(15, 0), new(15, 14)], .95)], 0x4C858DFF);
        WoodPath = GroundBrushRasterizer.Realize(new SpriteAssetId("opening-wood-path"), 22, 14, 64,
            [new GroundBrushStroke([new(2.5, 7.5), new(6, 8), new(13, 8), new(16.5, 8), new(18, 6), new(19.5, 2.5)], .62)], 0xB9A67BC8);
    }

    public SpriteAtlasResource Chest { get; } = TinyFarmChestArt.Create();
    public IReadOnlyList<SpriteFrameMetadata> ChestPoses { get; }
    public SpriteAtlasResource Title { get; }
    public SpriteAtlasResource Floors { get; }
    public SpriteAtlasResource Turnip { get; }
    public SpriteFrameMetadata TurnipPose { get; }
    public SpriteAtlasResource Props { get; }
    public IReadOnlyList<SpriteFrameMetadata> PropPoses { get; }
    public SpriteAtlasResource Bank { get; }
    public SpriteAtlasResource River { get; }
    public SpriteAtlasResource FarmPath { get; }
    public SpriteAtlasResource WoodPath { get; }
    public SpriteAtlasResource Mara { get; }
    public IReadOnlyList<SpriteFrameMetadata> MaraPoses { get; }
    public SpriteAtlasResource Slime { get; }
    public IReadOnlyList<SpriteFrameMetadata> SlimePoses { get; }
    public SpriteAtlasResource Gardener { get; }
    public IReadOnlyList<SpriteFrameMetadata> Poses { get; }
}
