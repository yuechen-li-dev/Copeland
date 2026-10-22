using Aurelian.Field2D;
using Aurelian.Spatial2D;
using TinyFarm.Native;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmLandscapeTests
{
    [Fact]
    public void RiverMeandersVariesWidthAndContinuesBeyondBothSceneEdges()
    {
        double[] rows = Enumerable.Range(0, 21).Select(index => index / 2.0).ToArray();
        Assert.True(rows.Select(TinyFarmLandscape.WestBank).Max() - rows.Select(TinyFarmLandscape.WestBank).Min() > 1);
        double[] widths = rows.Select(y => TinyFarmLandscape.EastBank(y) - TinyFarmLandscape.WestBank(y)).ToArray();
        Assert.True(widths.Max() - widths.Min() > 1);
        Assert.True(TinyFarmLandscape.IsRiver(12, -1));
        Assert.True(TinyFarmLandscape.IsRiver(12, 11));
        Assert.All(TinyFarmLandscape.FarmRoads, path => Assert.True(path.Centerline.Count > 20));
    }

    [Fact]
    public void CurrentChapterAdvancesTheExistingFluidSolverAtTheHostCadence()
    {
        using var app = new TinyFarm.TinyFarmApp(legacySaveDirectory: "unused");
        var host = app.Game.Host;
        host.Execute(new SetSimulationModeCommand(TinyFarmSimulationMode.Playing));
        var field = host.Session.Field;
        field.Energize(new ScenePosition(12 * 1024, 5 * 1024));
        string before = field.SemanticHash;
        long tick = field.Fluid.Tick;
        host.AdvanceHostTime(TimeSpan.FromMilliseconds(100));
        Assert.Equal(tick + 6, field.Fluid.Tick);
        Assert.NotEqual(before, field.SemanticHash);
    }

    [Fact]
    public void GameplayAndSolverDomainFollowTheSameSemanticPolygon()
    {
        var field = TinyFarmFieldRuntime.CreateDefault();
        double units = ScenePosition.UnitsPerTile;
        for (int y = 0; y < 100; y++)
        {
            for (int x = 0; x < 160; x++)
            {
                double worldX = (x + .37) / 10;
                double worldY = (y + .41) / 10;
                bool expected = TinyFarmLandscape.River.Contains(new SpatialPoint2D(worldX, worldY));
                var at = new ScenePosition((int)Math.Round(worldX * units), (int)Math.Round(worldY * units));
                Assert.Equal(TinyFarmLandscape.IsRiver(at.XUnits / units, at.YUnits / units),
                    field.Contains(TinyFarmSceneIds.Riverside, at));
                Assert.Equal(expected, TinyFarmLandscape.IsRiver(worldX, worldY));
                if (field.Contains(TinyFarmSceneIds.Riverside, at))
                {
                    Assert.True(field.Fluid.Sample(at.XUnits - field.Definition.Origin.XUnits,
                        at.YUnits - field.Definition.Origin.YUnits).Liquid);
                }
            }
        }
    }

    [Fact]
    public void FinePresentationFollowsGeometryWithoutChangingLiveFluid()
    {
        var field = TinyFarmFieldRuntime.CreateDefault();
        var projection = new TinyFarmRiverProjection();
        string before = field.SemanticHash;
        byte[] pixels = projection.Project(field);
        Assert.Equal(before, field.SemanticHash);
        for (int y = 0; y < TinyFarmRiverProjection.Height; y += 3)
        {
            for (int x = 0; x < TinyFarmRiverProjection.Width; x += 3)
            {
                double worldX = TinyFarmRiverProjection.Left + (x + .5) / TinyFarmRiverProjection.Density;
                double worldY = TinyFarmRiverProjection.Top + (y + .5) / TinyFarmRiverProjection.Density;
                if (Math.Abs(TinyFarmLandscape.RiverDistance(worldX, worldY)) < .01)
                {
                    continue;
                }
                Assert.Equal(TinyFarmLandscape.IsRiver(worldX, worldY),
                    pixels[(y * TinyFarmRiverProjection.Width + x) * 4 + 2] >= 128);
            }
        }
        field.Energize(new ScenePosition(12 * 1024, 5 * 1024));
        Assert.NotEqual(pixels, projection.Project(field));
    }

    [Fact]
    public void RectangularStreamSaveMigratesDynamicsByWorldPositionAndRejectsTamperedTopology()
    {
        var old = new ReactiveFluid2D(14, 22, 512);
        for (int y = 1; y < 21; y++)
        {
            for (int x = 1; x < 13; x++)
            {
                old.LiquidMask[x, y] = 1;
                old.ConductiveMask[x, y] = 1;
            }
        }
        old.HeightField[4, 10] = .3f;
        old.Charge[4, 10] = .7f;
        var snapshot = new TinyFarmFieldSnapshot(1, "tinyfarm.riverside.stream.v1", old.Snapshot() with { Tick = 123 }, 3, 2, 4);
        var migrated = TinyFarmFieldRuntime.Restore(snapshot);
        Assert.Equal("tinyfarm.riverside.stream.v2", migrated.Definition.Id);
        Assert.Equal(.3f, migrated.Fluid.HeightField[9, 13]);
        Assert.Equal(.7f, migrated.Fluid.Charge[9, 13]);
        Assert.Equal(123, migrated.Fluid.Tick);
        Assert.Equal(3, migrated.ChargedWaterCooldownRemaining);
        Assert.Equal(4, migrated.ChargedWaterContactCount);
        Assert.Equal(migrated.SemanticHash, TinyFarmFieldRuntime.Restore(migrated.Capture()).SemanticHash);
        snapshot.Fluid.LiquidMask[0] = 1;
        Assert.Throws<InvalidDataException>(() => TinyFarmFieldRuntime.Restore(snapshot));
    }

    [Fact]
    public void BakedGroundMatchesTheCurrentCompositorAndRejectsStaleGeometry()
    {
        string assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        var meadow = TinyFarmM24Assets.Load(Path.Combine(assets, "m24")).Meadow;
        var dirt = TinyFarmTerrainBake.LoadDirt(assets);
        var field = TinyFarmFieldRuntime.CreateDefault();
        using var app = new TinyFarm.TinyFarmApp(legacySaveDirectory: "unused");
        var study = new TinyFarmTerrainStudy(TinyFarmTerrainMode.Multiscale, meadow, dirt);
        string baked = Path.Combine(assets, "shoreline", "ground");
        foreach (var scene in new[] { TinyFarmSceneIds.Farm, TinyFarmSceneIds.Riverside })
        {
            var definition = app.Game.Definitions.Scenes.Get(scene);
            var frame = TinyFarmFrameProjector.Project(app.Game.State, app.Game.Definitions) with
            {
                ActiveScene = scene,
                SceneWidth = definition.Width,
                SceneHeight = definition.Height,
            };
            var generated = study.Get(frame, field);
            var loaded = TinyFarmTerrainBake.Read(baked, frame, meadow, dirt);
            Assert.Equal(generated.ContentHash, loaded.ContentHash);
            Assert.Equal(generated.Rgba8, loaded.Rgba8);
            Assert.Throws<InvalidDataException>(() => TinyFarmTerrainBake.Read(baked,
                frame with { SceneWidth = frame.SceneWidth + 1 }, meadow, dirt));
        }
    }
}
