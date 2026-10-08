using Aurelian.GameHost;
using InputMan.Core;
using TinyFarm.Core;
using TinyFarm.InputMan;
using System.Diagnostics;
using System.Text.Json;

namespace TinyFarm.Native;

internal static class TinyFarmAgentNativeProof
{
    public static void Run(string root, TinyFarmGame game, TinyFarmNativeWindow window,
        TinyFarmNativeRenderer renderer, AurelianGameHost host)
    {
        string directory = Path.Combine(root, "artifacts", "tinyfarm-agent-authoring");
        TinyFarmAgentInspector.WriteArtifacts(directory);
        window.InjectKey(KeyboardKey.Enter, true);
        host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
        window.InjectKey(KeyboardKey.Enter, false);
        for (int frame = 0; frame < 90; frame++)
        {
            host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
        }
        TinyFarmFrame projection = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        if (!projection.Actors.Any(actor => actor.Id.Value == "ivy")
            || !projection.Actors.Any(actor => actor.Id.Value == "garden-cache")
            || game.Host.Session.PassiveDominatusAgentCount != 2)
        {
            throw new InvalidOperationException("The native host did not activate/project both authored agents.");
        }
        game.Presentation.HudVisible = false;
        TinyFarmM25NativeProof.Capture(Path.Combine(directory, "authored-agents-world.png"), renderer, host);
        game.OpenInventory(false);
        TinyFarmM25NativeProof.Capture(Path.Combine(directory, "authored-agents-inventory.png"), renderer, host);

        void Key(KeyboardKey key)
        {
            window.InjectKey(key, true);
            host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
            window.InjectKey(key, false);
            host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
        }
        void AwaitCheckpoint()
        {
            var timer = Stopwatch.StartNew();
            while (game.SaveInProgress || game.LoadInProgress)
            {
                if (timer.Elapsed > TimeSpan.FromSeconds(15))
                {
                    throw new InvalidOperationException("Native authored-agent checkpoint did not complete.");
                }
                host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
            }
        }
        Key(KeyboardKey.F);
        AwaitCheckpoint();
        if (!game.HasSave)
        {
            throw new InvalidOperationException("The native application did not save its authored cast.");
        }
        game.Host.ExecuteIntent(new SetEquipmentIntent(EquipmentSlot.Tool, null));
        Key(KeyboardKey.N);
        AwaitCheckpoint();
        if (!TinyFarmEquipmentRules.IsEquipped(game.State, TinyFarmIds.Axe)
            || game.State.Actor(new ActorId("ivy")).Agent?.Level != 2
            || game.State.ProductCount(new ActorId("garden-cache"), TinyFarmIds.TurnipSeed) != 3
            || game.State.Version != TinyFarmState.AgentAuthoringSaveVersion)
        {
            throw new InvalidOperationException("The application checkpoint did not restore loadout and authored agent state.");
        }
        using (var stream = File.Create(Path.Combine(directory, "native-proof.json")))
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("outcome", "Success: native spawned agents and application save/load");
            writer.WriteBoolean("applicationCheckpointQualified", true);
            writer.WriteBoolean("authoredAgentsProjected", true);
            writer.WriteBoolean("reflectionSerializationDisabled", !JsonSerializer.IsReflectionEnabledByDefault);
            writer.WriteNumber("width", renderer.Layout.Width);
            writer.WriteNumber("height", renderer.Layout.Height);
            writer.WriteString("semanticHash", TinyFarmSemanticHash.Compute(game.State));
            writer.WriteEndObject();
        }
        Console.WriteLine("TINYFARM_AGENT_NATIVE_QUALIFIED " + TinyFarmSemanticHash.Compute(game.State));
    }
}
