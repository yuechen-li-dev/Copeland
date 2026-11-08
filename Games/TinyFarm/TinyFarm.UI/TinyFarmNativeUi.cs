using Aurelian.GameWorld2D;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Pipeline;
using Machina.Presentation;
using Machina.Runtime.Input;
using Machina.Standard.Authoring;
using Machina.Standard.Text;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal sealed class TinyFarmNativeUi(TinyFarmGame game)
{
    private IMachinaTextMeasurer textMeasurer = MachinaTextMeasurers.Deterministic;
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

    public void SetTextMeasurer(IMachinaTextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        textMeasurer = measurer;
        key = null;
        resource = null;
        prepared = null;
    }

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
        nodes.Add(StandardUI.Label($"{frame.CurrentLocationName}  /  {frame.Time}", 0, 0, 400,
            id: "clock", color: TinyFarmUiTheme.Text));
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
        nodes.Add(StandardUI.Panel(0, 0, width, 32, id: "prompt", style: TinyFarmUiTheme.Panel(0x203D32EE)));
        nodes.Add(StandardUI.Label(prompt, 12, 6, width - 24,
            id: "prompt-text", color: ColorToken.Hex(0xFFF0BEFF)));
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
            return TinyFarmMenuPresentation.Build(game, textMeasurer);
        }
        return BuildHud();
    }

    /// <summary>Play: health and tool, the journal, the status line and Mara's dialogue beside her portrait.</summary>
    private UiNode BuildHud()
    {
        var nodes = new List<UiNode>();
        int health = game.State.Slice?.Health ?? 0;
        ActorState player = game.State.Actor(TinyFarmIds.Player);
        nodes.Add(StandardUI.Panel(24, 72, 225, 40, id: "health", style: TinyFarmUiTheme.Panel(), children:
        [
            StandardUI.Label($"HP {health}/12  SP {player.Rpg?.SpiritCurrent ?? 0}/{player.Rpg?.SpiritMaximum ?? 0}",
                12, 10, 210, id: "health-text", color: TinyFarmUiTheme.Accent),
        ]));
        string tool = game.State.SelectedHotbarSlot switch
        {
            1 => "TOOL: SEEDS / 1 + K",
            3 => "TOOL: AXE / 3 + K",
            4 => "SWORD / J",
            _ => "TOOL: TURNIP / 2 + K"
        };
        nodes.Add(StandardUI.Label(tool, 36, 122, 210, id: "selected-tool",
            color: TinyFarmUiTheme.Accent));
        Journal(nodes);
        nodes.Add(StandardUI.Panel(24, 662, 910, 36, id: "slice-status", style: TinyFarmUiTheme.Panel(), children:
        [
            StandardUI.Label(game.Status, 12, 10, 890, id: "slice-status-text",
                color: TinyFarmUiTheme.Text),
        ]));
        if (game.Dialogue.Presentation is { } dialogue)
        {
            // Keep the panel's bottom fixed; measured body and choices determine its height.
            string speaker = game.Dialogue.SpeakingActor is ActorId actor ? game.State.Actor(actor).Name : string.Empty;
            RectNode body = StandardUI.Paragraph(dialogue.Text, 790, id: "dialogue-body",
                foreground: TinyFarmUiTheme.Text, measurer: textMeasurer);
            var children = new List<UiNode>
            {
                StandardUI.Label(speaker.ToUpperInvariant(), 28, 16, 780, id: "speaker",
                    color: TinyFarmUiTheme.Accent),
                StandardUI.Place(body, 28, 48),
            };
            double nextY = 48 + body.Height!.Value + 8;
            for (int index = 0; index < dialogue.Choices.Count; index++)
            {
                bool selected = dialogue.SelectedChoiceIndex == index;
                children.Add(StandardUI.Label((selected ? ">  " : "    ") + dialogue.Choices[index].Text,
                    46, nextY, 760, height: 28, id: "choice-" + index,
                    color: ColorToken.Hex(selected ? 0xFFF0BEFFu : 0xCEDCCFFFu)));
                nextY += 28;
            }
            double panelHeight = Math.Max(172, nextY + 48);
            children.Add(StandardUI.Label("CLICK or SPACE next   /   UP-DOWN choose   /   ESC leave",
                28, panelHeight - 32, 790, id: "dialogue-hint", color: TinyFarmUiTheme.Text));
            nodes.Add(StandardUI.Panel(250, 622 - panelHeight, 850, panelHeight,
                id: "dialogue", children: children, style: TinyFarmUiTheme.Panel(0x19362BF5)));
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
        var children = new List<UiNode>
        {
            StandardUI.Label(game.JournalTitle, 18, 10, 310, id: "journal-title",
                color: TinyFarmUiTheme.Accent),
        };
        double nextY = 44;
        for (int index = 0; index < objectives.Length; index++)
        {
            RectNode paragraph = StandardUI.Paragraph(objectives[index], 316, id: "objective-" + index,
                variant: MachinaTextVariant.Caption, foreground: TinyFarmUiTheme.Text, measurer: textMeasurer);
            children.Add(StandardUI.Place(paragraph, 18, nextY));
            nextY += paragraph.Height!.Value + 6;
        }
        nodes.Add(StandardUI.Panel(912, 96, 344, nextY + 8,
            id: "journal", children: children, style: TinyFarmUiTheme.Panel()));
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
