using Aurelian.GameWorld2D;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.NativeComposition;
using TinyFarm.Core;

namespace TinyFarm.Native;

internal sealed partial class TinyFarmWorldPresenter
{
    private void AddSliceObjects(TinyFarmFrame frame, TimeSpan elapsed)
    {
        foreach (TinyFarmSceneObjectView item in frame.SceneObjects ?? [])
        {
            if (item.Id.Value == "farmhouse")
            {
                TinyFarmSceneObjectView doorway = frame.SceneObjects!.Single(candidate => candidate.Id.Value == "residence-entrance");
                worldSpriteScratch.Add(M24Sprite("opening-house", m24Assets.Farmhouse.Id,
                    new WorldPoint2(doorway.Position.X + doorway.Width / 2.0, doorway.Position.Y + .4),
                    elapsed, doorway.Position.Y, .78));
            }
            else if (item.Kind is SceneObjectKind.Bed or SceneObjectKind.CookingStation or SceneObjectKind.Plot)
            {
                int pose = item.Kind switch
                {
                    SceneObjectKind.Bed => 0,
                    SceneObjectKind.CookingStation => 1,
                    _ => 2
                };
                double feetY = item.Position.Y + item.Height;
                worldSpriteScratch.Add(new WorldSprite(new WorldPresentationId("opening-prop-" + item.Id.Value),
                    new WorldPoint2(item.Position.X + item.Width / 2.0, feetY), sliceArt!.Props.Id,
                    pose.ToString(), null, elapsed, false, 1, Native2DTint.White,
                    item.Kind == SceneObjectKind.Plot ? WorldSpriteLayer.Ground : WorldSpriteLayer.Actors, feetY, false));
            }
            else if (item.Id.Value == "hill")
            {
                for (int tree = 0; tree < 3; tree++)
                {
                    AddTree("opening-thicket-" + tree, item.Position.X + .5 + tree,
                        item.Position.Y + item.Height, elapsed, .7);
                }
            }
            else if (item.Id.Value == "fence")
            {
                for (int section = 0; section < 4; section++)
                {
                    double x = item.Position.X + .75 + section * 1.5;
                    double feetY = item.Position.Y + 1;
                    worldSpriteScratch.Add(new WorldSprite(new WorldPresentationId("opening-fence-" + section),
                        new WorldPoint2(x, feetY), sliceArt!.Props.Id, "3", null, elapsed, false, 1,
                        Native2DTint.White, WorldSpriteLayer.Actors, feetY, false));
                }
            }
            else if (item.Kind == SceneObjectKind.Tree && !item.Depleted)
            {
                AddTree("opening-" + item.Id.Value, item.Position.X + .5, item.Position.Y + 1, elapsed, .72);
            }
        }
        // Canopies outside the walking area frame the scene. They do not invent trunk collision.
        if (frame.ActiveScene == TinyFarmSceneIds.Farm || frame.ActiveScene == TinyFarmSceneIds.Overworld)
        {
            for (int x = 0; x <= frame.SceneWidth; x += 3)
            {
                AddTree("opening-north-" + x, x, -.15, elapsed, .8 + (x % 4) * .03);
                AddTree("opening-south-" + x, x, frame.SceneHeight + 2.4, elapsed, .74);
            }
        }
    }

    private void PresentSliceGround(NativeLayerFrameContext context, TinyFarmFrame frame, bool cave, bool house)
    {
        if (!cave && !house)
        {
            context.Present(painterly, pass => pass.SubmitQuad(new NativeQuadSubmission(
                new Native2DRect(left - 4 * scale, top - 4 * scale,
                    (frame.SceneWidth + 8) * scale, (frame.SceneHeight + 8) * scale),
                Native2DUvRect.Full, ResolveTexture(m24Assets.Meadow.Id), new Native2DTint(.83f, .94f, .87f, 1))));
        }
        if (cave || house)
        {
            context.Present(painterly, pass => pass.SubmitQuad(new NativeQuadSubmission(
                new Native2DRect(left, top, frame.SceneWidth * scale, frame.SceneHeight * scale),
                cave ? new Native2DUvRect(.5f, 0, 1, 1) : new Native2DUvRect(0, 0, .5f, 1),
                painterlyResources.Get(sliceArt!.Floors.Id), Native2DTint.White)));
        }
        if (frame.ActiveScene == TinyFarmSceneIds.Overworld)
        {
            foreach (SpriteAtlasResource patch in new[] { sliceArt!.Bank, sliceArt.River })
            {
                context.Present(painterly, pass => pass.SubmitQuad(new NativeQuadSubmission(
                    new Native2DRect(left, top, frame.SceneWidth * scale, frame.SceneHeight * scale),
                    Native2DUvRect.Full, painterlyResources.Get(patch.Id), Native2DTint.White)));
            }
        }
        if (frame.ActiveScene == TinyFarmSceneIds.Farm || frame.ActiveScene == TinyFarmSceneIds.Overworld)
        {
            SpriteAtlasResource path = frame.ActiveScene == TinyFarmSceneIds.Farm ? sliceArt!.FarmPath : sliceArt!.WoodPath;
            context.Present(painterly, pass => pass.SubmitQuad(new NativeQuadSubmission(
                new Native2DRect(left, top, frame.SceneWidth * scale, frame.SceneHeight * scale),
                Native2DUvRect.Full, painterlyResources.Get(path.Id), Native2DTint.White)));
        }
        if (frame.ActiveScene == TinyFarmSceneIds.Overworld)
        {
            // Painted wooden deck lies above the bank and water, within the semantic dry crossing.
            context.Present(painterly, pass => pass.SubmitQuad(new NativeQuadSubmission(
                new Native2DRect(left + 13.8f * scale, top + 7.15f * scale, 2.4f * scale, 1.7f * scale),
                new Native2DUvRect(0, 0, .5f, 1), painterlyResources.Get(sliceArt!.Floors.Id),
                new Native2DTint(.85f, .9f, .8f, 1))));
        }
        context.Present(shapes, pass =>
        {
            if (frame.ActiveScene == TinyFarmSceneIds.Overworld)
            {
                Tile(pass, 13.8f, 7.15f, 2.4f, .1f, 0xD6B77AFF, scale * .03f);
                Tile(pass, 13.8f, 8.75f, 2.4f, .1f, 0xD6B77AFF, scale * .03f);
                Tile(pass, 18.4f, 1.4f, 2.2f, 2.1f, 0x273D34FF, scale * .65f);
                Tile(pass, 18.9f, 1.8f, 1.2f, 1.7f, 0x101F1FFF, scale * .5f);
            }
            foreach (TinyFarmSceneObjectView item in frame.SceneObjects ?? [])
            {
                if (item.Id.Value.StartsWith("opening-river-", StringComparison.Ordinal) || item.Id.Value == "hill")
                {
                    continue;
                }
                if (item.Id.Value == "farmhouse" || item.Kind is SceneObjectKind.Tree or SceneObjectKind.Enemy)
                {
                    continue;
                }
                if (item.Kind == SceneObjectKind.Portal)
                {
                    // A threshold, not a giant opaque sign in the walking area.
                    Tile(pass, item.Position.X + .13f, item.Position.Y + .4f, item.Width - .26f, .3f,
                        0xC9AD75A0, scale * .1f);
                    continue;
                }
                if (item.Id.Value == "fence" || item.Kind is SceneObjectKind.Bed or SceneObjectKind.CookingStation or SceneObjectKind.Plot)
                {
                    continue;
                }
                DrawObject(pass, item, cave);
            }
            foreach (TinyFarmActorView actor in frame.Actors)
            {
                Tile(pass, actor.Position.X / 1024f - .36f, actor.Position.Y / 1024f - .09f,
                    .72f, .22f, 0x182E3260, scale * .11f);
            }
        });
    }

    private void PresentSliceCrops(NativeLayerFrameContext context, TinyFarmFrame frame)
    {
        context.Present(painterly, pass =>
        {
            foreach (TinyFarmPlotView plot in frame.Plots)
            {
                if (plot.Crop is null)
                {
                    continue;
                }
                float x = plot.Position.X + .5f;
                float y = plot.Position.Y + .85f;
                FarmPlotState truth = game.State.FarmPlots.Single(candidate => candidate.Id == plot.Id);
                bool ripe = truth.GrowthStage >= game.Definitions.Crop(truth.Crop!.Value).GrowthDays;
                SpriteFrameMetadata pose = sliceArt!.TurnipPose;
                float sourceFraction = ripe ? 1 : .58f;
                float size = (float)pose.Scale * scale / 48 * (ripe ? 1 : .6f);
                float width = pose.Width * size;
                float height = pose.Height * size * sourceFraction;
                UvRect uv = pose.Uv;
                pass.SubmitQuad(new NativeQuadSubmission(
                    new Native2DRect(left + x * scale - width / 2, top + y * scale - height, width, height),
                    new Native2DUvRect((float)uv.U0, (float)uv.V0, (float)uv.U1,
                        (float)(uv.V0 + (uv.V1 - uv.V0) * sourceFraction)),
                    painterlyResources.Get(sliceArt.Turnip.Id), Native2DTint.White));
            }
        });
        context.Present(shapes, pass =>
        {
            foreach (TinyFarmItemView item in frame.GroundItems)
            {
                float x = item.Position.X / 1024.0f;
                float y = item.Position.Y / 1024.0f;
                bool card = game.State.Item(item.Id).TeachesRecipe is not null;
                Tile(pass, x - .22f, y - .25f, .44f, .5f, card ? 0xF4DEAEFF : 0x82BB77FF, 2);
                if (card)
                {
                    Tile(pass, x - .14f, y - .12f, .28f, .04f, 0x74624AFF, 1);
                    Tile(pass, x - .14f, y + .02f, .2f, .04f, 0x74624AFF, 1);
                }
            }
            foreach (TinyFarmSceneObjectView item in frame.SceneObjects ?? [])
            {
                if (item.Id.Value == TinyFarmCraftingContent.SaltOutcrop.Value)
                {
                    Tile(pass, item.Position.X + .08f, item.Position.Y + .3f, .82f, .55f, 0x899488FF, 8);
                    if (!item.Depleted)
                    {
                        Tile(pass, item.Position.X + .2f, item.Position.Y + .18f, .32f, .3f, 0xE1DCCBFF, 3);
                        Tile(pass, item.Position.X + .55f, item.Position.Y + .38f, .23f, .24f, 0xF0E8D6FF, 3);
                    }
                }
            }
            foreach (TinyFarmPlotView plot in frame.Plots)
            {
                FarmPlotState truth = game.State.FarmPlots.Single(candidate => candidate.Id == plot.Id);
                if (truth.WateredToday)
                {
                    Tile(pass, plot.Position.X + .75f, plot.Position.Y + .68f, .12f, .12f,
                        0xB0D7D3FF, scale * .06f);
                }
            }
        });
    }

    private void PresentSliceTelegraph(NativeLayerFrameContext context, TinyFarmFrame frame)
    {
        if (frame.ActiveScene != TinyFarmSceneIds.DungeonEntrance
            || game.State.Enemy(TinyFarmIds.DungeonSlime).Lifecycle != EnemyLifecycle.Alive)
        {
            return;
        }
        TinyFarmSliceState slice = game.State.Slice!;
        context.Present(shapes, pass =>
        {
            float x = slice.SlimePosition.XUnits / 1024f;
            float y = slice.SlimePosition.YUnits / 1024f;
            Tile(pass, x - .45f, y - .1f, .9f, .25f, 0x132B2B80, scale * .12f);
            if (slice.SlimePhase == SlimePhase.Windup)
            {
                float dx = slice.SlimeDirection.XUnits;
                float dy = slice.SlimeDirection.YUnits;
                float length = Math.Max(1, MathF.Sqrt(dx * dx + dy * dy));
                for (int point = 0; point < 9; point++)
                {
                    float amount = point * .19f;
                    Tile(pass, x + dx / length * amount - .18f, y + dy / length * amount - .1f,
                        .36f, .2f, point % 2 == 0 ? 0xF2C078C0u : 0xF2C07860u, scale * .1f);
                }
            }
            Tile(pass, x - .3f, y - 1.2f, .6f, .055f, 0x203532FF, 0);
            Tile(pass, x - .3f, y - 1.2f,
                .6f * game.State.Enemy(TinyFarmIds.DungeonSlime).CurrentHealth / 4, .055f, 0xE2CD93FF, 0);
        });
    }

    private void PresentSliceSword(NativeLayerFrameContext context, TinyFarmFrame frame)
    {
        TinyFarmSliceState slice = game.State.Slice!;
        if (slice.SwordTicks < 5)
        {
            return;
        }
        TinyFarmActorView player = frame.Actors.Single(actor => actor.IsPlayer);
        float direction = player.Facing switch
        {
            ActorFacing.Up => -MathF.PI / 2,
            ActorFacing.Left => MathF.PI,
            ActorFacing.Right => 0,
            _ => MathF.PI / 2
        };
        float sweep = (18 - slice.SwordTicks) / 13f;
        context.Present(shapes, pass =>
        {
            for (int trail = 0; trail < 7; trail++)
            {
                float angle = direction - .9f + Math.Clamp(sweep - trail * .04f, 0, 1) * 1.8f;
                float x = player.Position.X / 1024f + MathF.Cos(angle) * .95f;
                float y = player.Position.Y / 1024f + MathF.Sin(angle) * .95f;
                Tile(pass, x - .11f, y - .14f, .22f, .28f, trail == 0 ? 0xFFF4CDFFu : 0xEAF4DD60u, scale * .11f);
            }
        });
    }
}
