using Aurelian.GameWorld2D;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Pipeline;
using Machina.Presentation;
using Machina.Runtime.Input;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal sealed class TinyFarmNativeUi(TinyFarmGame game)
{
    private TinyFarmUiKey? key;
    private MachinaPresentationFrame? resource;
    private MachinaPreparedPresentation? prepared;
    private string? pressedAction;
    private ScrollbarInteractionState statsScroll = ScrollbarInteractionState.Default;
    private TinyFarmScreen pressedScreen;
    private string? clockKey;
    private MachinaPresentationFrame? clockResource;
    private string? promptKey;
    private MachinaPresentationFrame? promptResource;
    public int Rebuilds { get; private set; }

    public MachinaPresentationFrame Resource(TinyFarmFrame frame)
    {
        if (game.Screen == TinyFarmScreen.Inventory)
        {
            game.Menus.Rows(game.State, game.Definitions);
        }
        TinyFarmUiKey next = CreateKey(frame);
        if (key == next && resource is not null)
        {
            return resource;
        }
        key = next;
        Rebuilds++;
        prepared = new MachinaPresentationPipeline().Prepare(Build(frame), 1280, 720);
        resource = prepared.PresentationFrame;
        return resource;
    }

    public void Pointer(TinyFarmFrame frame, PointerPoint point, bool down)
    {
        if (ScrollStats(new UiPointerButtonChanged(point, UiPointerButton.Primary, down, UiModifiers.None)))
        {
            pressedAction = null;
            return;
        }
        Resource(frame);
        UiHitTestResult? hit = prepared!.HitTest.HitTest(point);
        string? action = hit?.Semantics?.Disabled == true ? null : hit?.Action.Name;
        if (down)
        {
            pressedAction = action;
            pressedScreen = game.Screen;
            return;
        }
        string? pressed = pressedAction;
        pressedAction = null;
        if (pressed is not null && action == pressed && game.Screen == pressedScreen)
        {
            game.DispatchMenu(action);
        }
    }

    public void ResetPointer()
    {
        pressedAction = null;
        statsScroll = ScrollbarInteractionState.Default;
    }

    internal bool ScrollStats(UiInputEvent input)
    {
        if (game.Screen != TinyFarmScreen.Stats)
        {
            statsScroll = ScrollbarInteractionState.Default;
            return false;
        }
        int count = game.Menus.PropertyRows(game.State, game.Definitions).Count;
        var geometry = TinyFarmStatsScrollbar.Geometry(count, game.Menus.StatsOffset);
        var interaction = new ScrollbarInteractionGeometry(geometry.TrackRect, geometry.ThumbRect,
            geometry.IsVisible, geometry.ScrollOffset, geometry.MaxScrollOffset);
        ScrollbarHitPart hit = ScrollbarHitPart.None;
        if (input.TryGetPointerPosition(out PointerPoint point))
        {
            if (Contains(geometry.ThumbRect, point))
            {
                hit = ScrollbarHitPart.Thumb;
            }
            else if (Contains(geometry.TrackRect, point))
            {
                hit = ScrollbarHitPart.Track;
            }
            else if (point.X >= 234 && point.X < 1164 && point.Y >= 254 && point.Y < 594)
            {
                hit = ScrollbarHitPart.Viewport;
            }
        }
        ScrollbarInteractionResult result = ScrollbarInteraction.Reduce(statsScroll,
            new ScrollbarInteractionContext(new ScrollbarInteractionTarget("agent-properties"), interaction,
                TinyFarmStatsScrollbar.ViewportHeight, WheelMultiplier: TinyFarmStatsScrollbar.RowHeight * 3), hit, input);
        statsScroll = result.State;
        if (result.RequestedScrollOffset is double requested)
        {
            int row = (int)Math.Round(requested / TinyFarmStatsScrollbar.RowHeight);
            game.Menus.ScrollStats(row - game.Menus.StatsOffset, count);
        }
        return result.Consumed;
    }

    private static bool Contains(Machina.Layout.Geometry.Rect rect, PointerPoint point)
    {
        return point.X >= rect.X && point.X < rect.X + rect.Width
            && point.Y >= rect.Y && point.Y < rect.Y + rect.Height;
    }

    public PointerPoint ActionCenter(TinyFarmFrame frame, string action)
    {
        Resource(frame);
        var entry = prepared!.Lowering.Actions.First(pair => pair.Value.Name == action);
        var rect = prepared.Resolved.Nodes[entry.Key].Rect;
        return new PointerPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    public string[] ActionNames(TinyFarmFrame frame)
    {
        Resource(frame);
        return prepared!.Lowering.Actions.Values.Select(action => action.Name).Distinct().ToArray();
    }

    public TinyFarmUiResources Resources(TinyFarmFrame frame)
    {
        return new TinyFarmUiResources(Resource(frame), ClockResource(frame), PromptResource(frame));
    }

    private MachinaPresentationFrame ClockResource(TinyFarmFrame frame)
    {
        string next = frame.CurrentLocationName + "\n" + frame.Time;
        if (clockKey == next && clockResource is not null)
        {
            return clockResource;
        }
        clockKey = next;
        var nodes = new List<UiNode>();
        Text(nodes, "clock", $"{frame.CurrentLocationName}  /  {frame.Time}", 0, 0, 400, TextSize.Md);
        clockResource = Prepare(UI.Surface(id: "clock-surface", width: 400, height: 34, children: nodes), 400, 34);
        return clockResource;
    }

    private MachinaPresentationFrame? PromptResource(TinyFarmFrame frame)
    {
        InteractionTarget? target = TinyFarmSpatialQueries.SelectInteractionTarget(
            game.State,
            TinyFarmIds.Player,
            game.Definitions.Scenes);
        string? prompt = target is null || game.CapturesGameplay
            ? null
            : target.Kind switch
            {
                InteractionTargetKind.Actor => "E  Talk to " + game.State.Actor(target.Actor!.Value).Name,
                InteractionTargetKind.Plot => PlotPrompt(target),
                InteractionTargetKind.Enemy => "J sword   /   SPACE dodge",
                InteractionTargetKind.Tree => "3 + K  Chop firewood",
                InteractionTargetKind.GroundItem => "E  Pick up " + game.State.Item(target.Item!.Value).Name,
                InteractionTargetKind.ForageNode => "E  Gather " + game.Definitions.Item(game.Definitions.ForageNode(target.ForageNode!.Value).Product).Name,
                InteractionTargetKind.Container => "E  Open " + game.State.Actor(target.Actor!.Value).Name,
                InteractionTargetKind.CookingStation => game.State.Version >= TinyFarmState.CraftingSaveVersion ? "E  Open stove" : "E  Cook turnip broth",
                InteractionTargetKind.Bed => "E  Sleep until morning",
                InteractionTargetKind.Portal => PortalPrompt(frame, target),
                _ => "E  Interact",
            };
        if (prompt is null)
        {
            return null;
        }
        if (promptKey == prompt && promptResource is not null)
        {
            return promptResource;
        }
        promptKey = prompt;
        var nodes = new List<UiNode>();
        int width = Math.Clamp(prompt.Length * 10 + 24, 100, 460);
        Panel(nodes, "prompt", 0, 0, width, 32, 0x203D32EE);
        Text(nodes, "prompt-text", prompt, 12, 6, width - 24, TextSize.Md, 0xFFF0BEFF);
        promptResource = Prepare(UI.Surface(id: "prompt-surface", width: 710, height: 38, children: nodes), 710, 38);
        return promptResource;
    }

    private static MachinaPresentationFrame Prepare(UiNode node, int width, int height)
    {
        return new MachinaPresentationPipeline().Prepare(node, width, height).PresentationFrame;
    }

    private string PlotPrompt(InteractionTarget target)
    {
        FarmPlotState plot = game.State.FarmPlots.Single(candidate => candidate.Id == target.Plot);
        if (plot.Crop is null)
        {
            return "1 + K  Plant turnip";
        }
        if (plot.GrowthStage >= game.Definitions.Crop(plot.Crop.Value).GrowthDays)
        {
            return "E  Harvest turnip";
        }
        return plot.WateredToday ? "Watered / grows after sleep" : "E  Water turnip";
    }

    private string PortalPrompt(TinyFarmFrame frame, InteractionTarget target)
    {
        TinyFarmRouteView route = frame.SceneRoutes!.Single(candidate => candidate.TriggerObject == target.SceneObject);
        if (!TinyFarmOpeningRoutes.IsOpen(game.State, route.TargetScene))
        {
            return route.TargetScene == TinyFarmSceneIds.Riverside
                ? "The river road / Mara knows the way"
                : "Unfinished road / closed in this slice";
        }
        return "E  " + route.InteractionLabel;
    }

    private TinyFarmUiKey CreateKey(TinyFarmFrame frame)
    {
        int objectives = game.JournalProgress;

        var inventoryHash = new HashCode();
        foreach (TinyFarmInventoryView item in frame.Inventory)
        {
            inventoryHash.Add(item.Id, StringComparer.Ordinal);
            inventoryHash.Add(item.Count);
        }

        return new TinyFarmUiKey(
            game.Screen,
            game.Status,
            frame.ActiveScene,
            game.State.SelectedHotbarSlot,
            objectives,
            game.Dialogue.Presentation?.OperationId,
            game.Dialogue.SelectedChoiceIndex,
            inventoryHash.ToHashCode(),
            game.State.Slice?.Health ?? 0,
            game.State.Slice?.LoopComplete ?? false,
            game.Menus.CacheKey + game.CraftCacheKey + game.ContainerCacheKey + "|" + game.TitleSelection,
            game.State.Equipment,
            game.SaveInProgress || game.LoadInProgress,
            game.MenuSaveAvailable);
    }

    private UiNode Build(TinyFarmFrame frame)
    {
        if (game.Screen is TinyFarmScreen.Inventory or TinyFarmScreen.Stats or TinyFarmScreen.Crafting or TinyFarmScreen.Container or TinyFarmScreen.Paused or TinyFarmScreen.Title)
        {
            return TinyFarmMenuPresentation.Build(game);
        }
        return BuildHud();
    }

    /// <summary>Play: health and tool, the journal, the status line and Mara's dialogue beside her portrait.</summary>
    private UiNode BuildHud()
    {
        var nodes = new List<UiNode>();
        int health = game.State.Slice?.Health ?? 0;
        ActorState player = game.State.Actor(TinyFarmIds.Player);
        Panel(nodes, "health", 24, 72, 225, 40, 0x19362BD9);
        Text(nodes, "health-text", $"HP {health}/12  SP {player.Rpg?.SpiritCurrent ?? 0}/{player.Rpg?.SpiritMaximum ?? 0}",
            36, 82, 210, TextSize.Md, 0xF5DDB5FF);
        string tool = game.State.SelectedHotbarSlot switch
        {
            1 => "TOOL: SEEDS / 1 + K",
            3 => "TOOL: AXE / 3 + K",
            4 => "SWORD / J",
            _ => "TOOL: TURNIP / 2 + K"
        };
        Text(nodes, "selected-tool", tool, 36, 122, 210, TextSize.Md, 0xF5DDB5FF);
        Journal(nodes);
        Panel(nodes, "slice-status", 24, 662, 910, 36, 0x19362BD9);
        Text(nodes, "slice-status-text", game.Status, 36, 672, 890, TextSize.Md);
        if (game.Dialogue.Presentation is { } dialogue)
        {
            // The portrait layer draws the speaking character at the panel's left.
            Panel(nodes, "dialogue", 250, 450, 850, 172, 0x19362BF5);
            string speaker = game.Dialogue.SpeakingActor is ActorId actor ? game.State.Actor(actor).Name : string.Empty;
            Text(nodes, "speaker", speaker.ToUpperInvariant(), 278, 466, 780, TextSize.Md, 0xF5DDB5FF);
            Lines(nodes, "dialogue-body", dialogue.Text, 278, 498, 790, 76, TextSize.Md);
            for (int index = 0; index < dialogue.Choices.Count; index++)
            {
                bool selected = dialogue.SelectedChoiceIndex == index;
                Text(nodes, "choice-" + index, (selected ? ">  " : "    ") + dialogue.Choices[index].Text,
                    296, 530 + index * 28, 760, TextSize.Md, selected ? 0xFFF0BEFF : 0xCEDCCFFF);
            }
            Text(nodes, "dialogue-hint", "CLICK or SPACE next   /   UP-DOWN choose   /   ESC leave", 278, 590, 790, TextSize.Md);
        }
        return UI.Surface(id: "opening-slice", width: 1280, height: 720, children: nodes);
    }

    /// <summary>The prototype's supper journal, now the journal for whichever chapter is under way.</summary>
    private void Journal(List<UiNode> nodes)
    {
        string[] objectives = game.Objectives();
        if (objectives.Length == 0 || TinyFarmSupper.IsComplete(game.State))
        {
            return;
        }
        bool openingDone = game.State.Slice is { LoopComplete: true };
        if (openingDone && !TinyFarmSupper.IsRequested(game.State))
        {
            return;
        }
        int panelIndex = nodes.Count;
        Text(nodes, "journal-title", game.JournalTitle, 930, 106, 310, TextSize.Md, 0xF5DDB5FF);
        int y = 140;
        foreach (string objective in objectives)
        {
            int rows = Lines(nodes, "objective-" + y, objective, 930, y, 316, 30, TextSize.Sm);
            y += rows * 25 + 6;
        }
        // The panel goes underneath the text, sized to what was written.
        var panel = new List<UiNode>();
        Panel(panel, "journal", 912, 96, 344, y - 96 + 8, 0x19362BD9);
        nodes.InsertRange(panelIndex, panel);
    }

    internal static void Panel(List<UiNode> nodes, string id, int x, int y, int width, int height, uint color = 0x163D31F5)
    {
        nodes.Add(UI.Anchor(UI.Rect(id: id, style: new UiStyle(Background: ColorToken.Hex(color),
            BorderColor: ColorToken.Hex(0x8DA48180), BorderThickness: 1, Shape: UiShapeKind.RoundedRect, CornerRadius: 12)),
            id: id + "-anchor", left: x, top: y, width: width, height: height));
    }

    internal static void Text(List<UiNode> nodes, string id, string text, int x, int y, int width, TextSize size, uint color = 0xE7EBDDFF)
    {
        nodes.Add(UI.Anchor(UI.Text(text, id: id, color: ColorToken.Hex(color), size: size),
            id: id + "-anchor", left: x, top: y, width: width, height: 34));
    }

    /// <summary>Word-wrapped text, 25 pixels per row. Returns the number of rows written.</summary>
    private static int Lines(List<UiNode> nodes, string id, string text, int x, int y, int width, int characters, TextSize size)
    {
        string line = "";
        int row = 0;
        foreach (string word in text.Split(' '))
        {
            if (line.Length + word.Length > characters && line.Length > 0)
            {
                Text(nodes, id + row, line, x, y + row * 25, width, size);
                row++;
                line = "";
            }
            line += (line.Length == 0 ? "" : " ") + word;
        }
        Text(nodes, id + row, line, x, y + row * 25, width, size);
        return row + 1;
    }
}

internal readonly record struct TinyFarmUiKey(
    TinyFarmScreen Screen,
    string Status,
    SceneId? ActiveScene,
    int SelectedHotbarSlot,
    int Objectives,
    string? DialogueOperationId,
    int DialogueSelectedChoiceIndex,
    int InventoryHash,
    int Health,
    bool LoopComplete,
    string MenuState,
    TinyFarmEquipment? Equipment,
    bool PersistenceBusy,
    bool SaveAvailable);

internal readonly record struct TinyFarmUiResources(
    MachinaPresentationFrame Base,
    MachinaPresentationFrame Clock,
    MachinaPresentationFrame? Prompt);
