using System.Security.Cryptography;
using System.Text.Json;
using Aurelian.Composition;
using Aurelian.Machina;
using Aurelian.Rendering.Contracts.Resolved2D;
using Aurelian.Rendering.Raster;
using TinyFarm.Presentation;
using Xunit;

namespace TinyFarm.Core.Tests;

/// <summary>
/// Executable integration sketch, not a game kit or native sprite qualification.
/// Camera, painter ordering and two-frame animation below are deliberately local:
/// they measure the machinery a new game currently has to reconstruct.
/// </summary>
public sealed class GameSubstrateAuditM0Tests
{
    [Fact]
    public void Scene_ResolvesInteractionAndCollision_ProjectsCameraAnimationAndMachinaOverlay()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.LoadM21();
        TinyFarmState state = TinyFarmM21ControlStates.Create(definitions);
        // Initial fixture authoring only. All subsequent gameplay uses the resolver.
        Place(state, TinyFarmIds.Player, new ScenePosition(12164, 6656), ActorFacing.Left);
        Place(state, TinyFarmIds.Mara, new ScenePosition(11140, 6656), ActorFacing.Right);
        var resolver = new TinyFarmResolver(definitions);
        long sequence = 0;

        ResolutionBatchResult Resolve(GameIntent intent)
        {
            ResolutionBatchResult result = resolver.Resolve(state,
                [new IntentEnvelope(TinyFarmIds.Player, intent, state.Minute, sequence++, IntentSourceKind.Human)]);
            state = result.State;
            return result;
        }

        IntentResult interaction = Assert.Single(Resolve(new InteractIntent()).Results);
        Assert.Equal(IntentResultStatus.Accepted, interaction.Status);
        Assert.Contains(interaction.Events, item =>
            item.Kind == GameEventKind.Conversation && item.Target == TinyFarmIds.Mara);

        string beforeCollision = TinyFarmSemanticHash.Compute(state);
        IntentResult collision = Assert.Single(Resolve(new SpatialMoveIntent(1, 0, 256)).Results);
        Assert.Equal(IntentReason.MovementBlocked, collision.Reason);
        Assert.Equal(beforeCollision, TinyFarmSemanticHash.Compute(state));

        TinyFarmFrame beforeMove = TinyFarmFrameProjector.Project(state, definitions);
        Assert.Equal(IntentResultStatus.Accepted,
            Assert.Single(Resolve(new SpatialMoveIntent(-1, 0, 128)).Results).Status);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(state, definitions);
        Assert.NotEqual(CameraX(beforeMove), CameraX(frame));
        Assert.Contains(frame.Actors, actor => actor.Id == TinyFarmIds.Mara);

        var surface = new LayerSurfaceDescriptor(1280, 720);
        var sink = new RecordingSink();
        var overlay = new TinyFarmMachinaUiLayer(sink, surface);
        using var compositor = new AurelianLayerCompositor(surface);
        compositor.Add(overlay);
        compositor.SendToLayer(new LayerMessage<TinyFarmPresentationSnapshot>(
            TinyFarmMachinaUiLayer.ApplicationId, TinyFarmMachinaUiLayer.Id,
            new TinyFarmPresentationSnapshot(
                TinyFarmPlayerUiProjector.Project(state, definitions), frame.Day, frame.Time,
                frame.CurrentLocationName, TinyFarmSimulationMode.Paused, false,
                "M0: conversation accepted; wall blocked", frame.InteractionHints, frame.Narrative)));
        compositor.Attach();
        Assert.True(compositor.RouteInput(new LayerKeyChanged(LayerKey.Number3, true)).Consumed);
        Assert.Equal(3, Assert.Single(sink.Commands).HotbarSlot);

        string projectionHash = TinyFarmSemanticHash.Compute(state);
        Resolved2DPlan hud = MachinaPresentationTranslator.Translate(overlay.Prepared.PresentationFrame);
        byte[] first = Render(frame, hud, TimeSpan.Zero);
        byte[] second = Render(frame, hud, TimeSpan.FromMilliseconds(150));
        Assert.NotEqual(SHA256.HashData(first), SHA256.HashData(second));
        Assert.Equal(first, Render(frame, hud, TimeSpan.Zero));
        Assert.Equal(projectionHash, TinyFarmSemanticHash.Compute(state));
        Assert.NotEmpty(hud.Operations);

        string? output = Environment.GetEnvironmentVariable("AURELIAN_GAME_AUDIT_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "scene-0.ppm"), first);
            File.WriteAllBytes(Path.Combine(output, "scene-1.ppm"), second);
            File.WriteAllText(Path.Combine(output, "probe.json"), JsonSerializer.Serialize(new
            {
                milestone = "AURELIAN-GAME-SUBSTRATE-AUDIT-M0",
                backend = "Aurelian CPU raster; native sprite path NOT qualified",
                interaction = interaction.Status.ToString(),
                collision = collision.Reason.ToString(),
                cameraBefore = CameraX(beforeMove),
                cameraAfter = CameraX(frame),
                npc = TinyFarmIds.Mara.Value,
                animatedObject = AnimatedObject(frame).Id.Value,
                animationFrames = new[] { 0, 1 },
                hudOperations = hud.Operations.Count,
                uiCommand = sink.Commands[0],
                stateHash = projectionHash,
                frameHashes = new[] { Convert.ToHexString(SHA256.HashData(first)), Convert.ToHexString(SHA256.HashData(second)) },
                reconstructedGlue = new[] { "camera projection", "world rectangle lowering", "painter order", "two-frame presentation clock" }
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static TinyFarmSceneObjectView AnimatedObject(TinyFarmFrame frame)
    {
        TinyFarmActorView player = frame.Actors.Single(actor => actor.IsPlayer);
        double playerTileX = (double)player.Position.X / frame.SceneUnitsPerTile;
        double playerTileY = (double)player.Position.Y / frame.SceneUnitsPerTile;
        return frame.SceneObjects!
            .Where(item => item.BlocksMovement)
            .OrderBy(item => Math.Abs(item.Position.X - playerTileX) + Math.Abs(item.Position.Y - playerTileY))
            .ThenBy(item => item.Id.Value, StringComparer.Ordinal)
            .First();
    }

    private static double CameraX(TinyFarmFrame frame)
    {
        TinyFarmActorView player = frame.Actors.Single(actor => actor.IsPlayer);
        // Deliberately magnified viewport to pressure follow rather than fit the whole room.
        return Math.Max(0, (player.Position.X * 32.0 / frame.SceneUnitsPerTile) - 160);
    }

    private static byte[] Render(TinyFarmFrame frame, Resolved2DPlan hud, TimeSpan elapsed)
    {
        const double zoom = 3;
        double cameraX = CameraX(frame);
        double cameraY = Math.Max(0, (frame.Actors.Single(actor => actor.IsPlayer).Position.Y * 32.0 / frame.SceneUnitsPerTile) - 90);
        int animationFrame = (int)(elapsed.Ticks / TimeSpan.FromMilliseconds(150).Ticks % 2);
        var operations = new List<Resolved2DOperation>
        {
            new FillRectangleOperation("ground", new Resolved2DRectangle(0, 0, 1280, 720), new(38, 66, 42, 255))
        };
        TinyFarmSceneObjectView animated = AnimatedObject(frame);
        foreach (TinyFarmSceneObjectView item in frame.SceneObjects!.OrderBy(item => item.Layer).ThenBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            Resolved2DRgbaColor color = item.BlocksMovement ? new(110, 90, 65, 255) : new(70, 100, 55, 255);
            if (item.Id == animated.Id && animationFrame == 1)
            {
                color = new(160, 120, 65, 255);
            }
            operations.Add(new FillRectangleOperation(item.Id.Value,
                new Resolved2DRectangle((item.Position.X * 32 - cameraX) * zoom, (item.Position.Y * 32 - cameraY) * zoom,
                    item.Width * 32 * zoom, item.Height * 32 * zoom), color));
        }
        foreach (TinyFarmActorView actor in frame.Actors.OrderBy(actor => actor.Position.Y).ThenBy(actor => actor.Id.Value, StringComparer.Ordinal))
        {
            operations.Add(new FillRectangleOperation(actor.Id.Value,
                new Resolved2DRectangle(
                    (actor.Position.X * 32.0 / frame.SceneUnitsPerTile - cameraX) * zoom - 12,
                    (actor.Position.Y * 32.0 / frame.SceneUnitsPerTile - cameraY) * zoom - 24, 24, 30),
                actor.IsPlayer ? new(90, 210, 250, 255) : new(240, 175, 95, 255)));
        }
        operations.AddRange(hud.Operations);
        RasterFrame raster = new AurelianCpuRasterRenderer().Render(new Resolved2DPlan(hud.Viewport, operations));
        return RasterPpmEncoder.EncodeP6(raster.Surface);
    }

    private static void Place(TinyFarmState state, ActorId actor, ScenePosition position, ActorFacing facing)
    {
        int sceneIndex = state.MutableActorScenes.FindIndex(item => item.Actor == actor);
        state.MutableActorScenes[sceneIndex] = new ActorSceneState(actor, TinyFarmSceneIds.Farm, position, facing);
        int actorIndex = state.MutableActors.FindIndex(item => item.Id == actor);
        state.MutableActors[actorIndex] = state.MutableActors[actorIndex] with { Location = TinyFarmIds.Farmhouse };
    }

    private sealed class RecordingSink : ILayerApplicationMessageSink
    {
        public List<TinyFarmUiCommandDto> Commands { get; } = [];

        public void Publish<TPayload>(LayerMessage<TPayload> message)
        {
            Commands.Add(Assert.IsType<TinyFarmUiCommandDto>(message.Payload));
        }
    }
}
