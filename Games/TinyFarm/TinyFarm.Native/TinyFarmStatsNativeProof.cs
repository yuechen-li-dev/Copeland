using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Composition;
using Aurelian.GameHost;
using InputMan.Core;
using Silk.NET.Maths;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal sealed record TinyFarmStatsProof(
    string Outcome, string Device, bool GameplayMapExcluded, bool TextMapExclusive,
    bool TextDoesNotMoveAgent, bool SemanticWorldAndFieldUnchanged,
    bool ScrollbarWheelAndDragQualified, bool AgentSelectionQualified,
    bool SaveLoadQualified, bool HudIndependent, bool ResizeQualified,
    bool ReflectionSerializationDisabled, int SaveVersion, string[] Captures, string SemanticHash);

[JsonSerializable(typeof(TinyFarmStatsProof))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class TinyFarmStatsProofJsonContext : JsonSerializerContext;

internal static class TinyFarmStatsNativeProof
{
    public static void Run(string root, TinyFarmGame game, TinyFarmNativeWindow window,
        TinyFarmNativeRenderer renderer, AurelianGameHost host)
    {
        string output = Path.Combine(root, "artifacts", "tinyfarm-rpg-gate-a1");
        Directory.CreateDirectory(output);
        void Frame() => host.RunFrame(TimeSpan.FromSeconds(1.0 / 60));
        void Key(KeyboardKey key)
        {
            window.InjectKey(key, true);
            Frame();
            window.InjectKey(key, false);
            Frame();
        }
        void Click(string action)
        {
            LayerPoint point = renderer.MenuActionCenter(action);
            window.MenuInput.Enqueue(new LayerPointerButtonChanged(point, LayerPointerButton.Primary, true));
            Frame();
            window.MenuInput.Enqueue(new LayerPointerButtonChanged(point, LayerPointerButton.Primary, false));
            Frame();
        }
        void Capture(string name) => TinyFarmM25NativeProof.Capture(Path.Combine(output, name), renderer, host);
        void SelectAgent(ActorId id)
        {
            for (int attempt = 0; attempt < game.State.Actors.Count && game.Menus.StatsAgent != id; attempt++)
            {
                Click("agent:next");
            }
            Require(game.Menus.StatsAgent == id, "Agent selector could not reach " + id.Value);
        }
        LayerPoint Point(double x, double y) => new(
            (renderer.Layout.UiLeft + x * renderer.Layout.UiScale) * window.NativeWindow.Size.X / renderer.Layout.Width,
            (renderer.Layout.UiTop + y * renderer.Layout.UiScale) * window.NativeWindow.Size.Y / renderer.Layout.Height);
        void AwaitPersistence()
        {
            var timer = Stopwatch.StartNew();
            while (game.SaveInProgress || game.LoadInProgress)
            {
                Require(timer.Elapsed < TimeSpan.FromSeconds(15), "Checkpoint operation timed out.");
                Frame();
            }
        }

        Key(KeyboardKey.Enter);
        Key(KeyboardKey.C);
        Require(game.Screen == TinyFarmScreen.Stats, "C did not open agent properties.");
        Require(!game.Contexts.Contains(GameControls.Gameplay), "Stats left the gameplay map active.");
        string before = Hash(game);
        ScenePosition position = game.State.ActorScene(TinyFarmIds.Player).WorldPosition;
        Click("group:All");
        Capture("agent-properties-1080p.png");
        Key(KeyboardKey.W);
        Key(KeyboardKey.J);
        Require(Hash(game) == before, "Stats input altered the world.");
        Click("group:Stats");
        Capture("agent-stats-1080p.png");
        Click("group:All");
        Click("search");
        Require(game.Contexts.SequenceEqual([GameControls.System, GameControls.TextEntry]), "Text entry did not own its map context.");
        window.MenuInput.Enqueue(new LayerTextEntered("wisdom"));
        Frame();
        foreach (KeyboardKey key in new[] { KeyboardKey.W, KeyboardKey.I, KeyboardKey.C,
            KeyboardKey.E, KeyboardKey.J, KeyboardKey.F, KeyboardKey.N, KeyboardKey.Q })
        {
            Key(key);
        }
        Require(game.Screen == TinyFarmScreen.Stats && game.Menus.SearchFocused && !game.ShouldQuit
            && !game.SaveInProgress && !game.LoadInProgress, "Text leaked into UI, gameplay or shortcuts.");
        Require(game.State.ActorScene(TinyFarmIds.Player).WorldPosition == position && Hash(game) == before,
            "Typing moved the player or changed semantic state.");
        Require(game.Menus.PropertyRows(game.State, game.Definitions).Count == 1, "Property filter failed.");
        Capture("agent-filter-1080p.png");
        Key(KeyboardKey.Escape);
        Require(game.Screen == TinyFarmScreen.Stats && !game.Menus.SearchFocused, "Escape closed the menu instead of leaving typing.");
        Click("clear");
        window.MenuInput.Enqueue(new LayerPointerWheel(Point(500, 300), 0, -1));
        Frame();
        Require(game.Menus.StatsOffset == 3, "Native wheel did not scroll the table.");
        int count = game.Menus.PropertyRows(game.State, game.Definitions).Count;
        var geometry = TinyFarmStatsScrollbar.Geometry(count, game.Menus.StatsOffset);
        LayerPoint thumb = Point(1190, geometry.ThumbRect.Y + 5);
        window.MenuInput.Enqueue(new LayerPointerButtonChanged(thumb, LayerPointerButton.Primary, true));
        Frame();
        LayerPoint bottom = Point(1190, 590);
        window.MenuInput.Enqueue(new LayerPointerMoved(bottom, thumb));
        Frame();
        window.MenuInput.Enqueue(new LayerPointerButtonChanged(bottom, LayerPointerButton.Primary, false));
        Frame();
        Require(game.Menus.StatsOffset == count - TinyFarmMenus.PageSize, "Native thumb drag did not reach the bottom.");
        Capture("agent-properties-scrolled.png");

        SelectAgent(new ActorId("ivy"));
        Click("group:Stats");
        Require(game.Menus.PropertyRows(game.State, game.Definitions).Any(row => row.Name == "Strength" && row.Value == "11"),
            "Agent selection failed to expose Ivy's authored abilities.");
        Capture("ivy-stats-1080p.png");
        Click("group:Traits");
        Capture("ivy-traits-1080p.png");
        Click("group:Conditions");
        Capture("ivy-conditions-1080p.png");
        SelectAgent(new ActorId("garden-cache"));
        Click("group:All");
        Capture("object-properties-1080p.png");
        Require(game.Menus.PropertyRows(game.State, game.Definitions).All(row => row.Name != "Strength"),
            "Object acquired character stats.");
        Key(KeyboardKey.F9);
        Capture("properties-hud-hidden.png");
        Require(game.Screen == TinyFarmScreen.Stats && Hash(game) == before, "HUD visibility interfered with properties or world.");
        Key(KeyboardKey.F9);

        SelectAgent(new ActorId("ivy"));
        Click("group:Skills");
        window.NativeWindow.Size = new Vector2D<int>(2560, 1440);
        window.NativeWindow.DoEvents();
        Frame();
        Require(renderer.Layout.Width == 2560 && renderer.Layout.Height == 1440, "1440p resize failed.");
        Capture("agent-skills-1440p.png");
        window.NativeWindow.Size = new Vector2D<int>(1600, 1000);
        window.NativeWindow.DoEvents();
        Frame();
        Click("group:Stats");
        Capture("agent-stats-1600x1000.png");
        Require(game.Menus.StatsGroup == "Stats" && Hash(game) == before, "Resize changed input projection or semantic state.");

        Key(KeyboardKey.C);
        Key(KeyboardKey.Escape);
        Require(game.Screen == TinyFarmScreen.Paused, "Stats did not return to play.");
        Click("stats");
        Key(KeyboardKey.Escape);
        Require(game.Screen == TinyFarmScreen.Paused, "Stats did not return to pause parent.");
        string saved = TinyFarmSemanticHash.Compute(game.State);
        Click("save");
        AwaitPersistence();
        Click("load");
        Click("confirm");
        AwaitPersistence();
        Require(TinyFarmSemanticHash.Compute(game.State) == saved
            && game.State.Actor(new ActorId("ivy")).Rpg?.BaseAbilities.Strength == 11, "Native checkpoint did not restore the RPG profile.");
        var proof = new TinyFarmStatsProof("A: agent properties and action-map isolation qualified", renderer.Device,
            true, true, true, true, true, true, true, true, true, !JsonSerializer.IsReflectionEnabledByDefault,
            game.State.Version, ["agent-properties-1080p.png", "agent-stats-1080p.png", "agent-filter-1080p.png",
                "agent-properties-scrolled.png", "ivy-stats-1080p.png", "ivy-traits-1080p.png",
                "ivy-conditions-1080p.png", "object-properties-1080p.png", "properties-hud-hidden.png",
                "agent-skills-1440p.png", "agent-stats-1600x1000.png"], Hash(game));
        File.WriteAllText(Path.Combine(output, "native-proof.json"),
            JsonSerializer.Serialize(proof, TinyFarmStatsProofJsonContext.Default.TinyFarmStatsProof));
        Console.WriteLine("TINYFARM_RPG_A1_NATIVE_QUALIFIED " + proof.SemanticHash);
    }

    private static string Hash(TinyFarmGame game) => TinyFarmSemanticHash.Compute(game.State) + ":" + game.Host.Session.Field.SemanticHash;

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
