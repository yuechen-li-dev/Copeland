using Aurelian.GameWorld2D;
using Machina.Core.Authoring;
using Machina.Core.Actions;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Pipeline;
using Machina.Presentation;
using Machina.Runtime.Input;
using Machina.Runtime.State;
using Machina.Standard.Authoring;
using Machina.Standard.Text;
using TinyFarm.Core;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

internal sealed class TinyFarmNativeUi(TinyFarmGame game)
{
    private enum ViewLayer
    {
        Base,
        Clock,
        Prompt,
        Inspector,
    }

    private static readonly UiIdFamily<ViewLayer> CacheIds = new("presentation", value => value.ToString());
    private static readonly UiStateKey<UiPointerActivation> PointerKey = new("input.pointer", UiStateLifetime.Activation);
    private static readonly UiStateKey<UiState<ScrollbarInteractionState>> DragKey = new("input.stats-drag", UiStateLifetime.Activation);

    private IMachinaTextMeasurer textMeasurer = MachinaTextMeasurers.Deterministic;
    public int Rebuilds { get; private set; }

    private UiPointerActivation PressState => game.UiScope.GetOrCreate(PointerKey,
        () => new UiPointerActivation());
    private UiState<ScrollbarInteractionState> StatsDrag => game.UiScope.GetOrCreate(DragKey,
        () => new UiState<ScrollbarInteractionState>(ScrollbarInteractionState.Default));

    public void SetTextMeasurer(IMachinaTextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        textMeasurer = measurer;
        Cache(ViewLayer.Base).Invalidate();
    }

    public MachinaPresentationFrame Resource(TinyFarmFrame frame)
    {
        return PrepareBase(frame).PresentationFrame;
    }

    private MachinaPreparedPresentation PrepareBase(TinyFarmFrame frame)
    {
        MachinaPresentationCache cache = Cache(ViewLayer.Base);
        int before = cache.Preparations;
        MachinaPreparedPresentation prepared = cache.Prepare(Build(frame), 1280, 720);
        Rebuilds += cache.Preparations - before;
        return prepared;
    }

    private MachinaPresentationCache Cache(ViewLayer layer)
    {
        return game.UiScope.GetOrCreate(new UiStateKey<MachinaPresentationCache>(CacheIds[layer]), () => new MachinaPresentationCache());
    }

    public void Pointer(TinyFarmFrame frame, PointerPoint point, bool down)
    {
        if (ScrollStats(new UiPointerButtonChanged(point, UiPointerButton.Primary, down, UiModifiers.None)))
        {
            PressState.Reset();
            return;
        }
        MachinaPreparedPresentation prepared = PrepareBase(frame);
        UiHitTestResult? hit = prepared.HitTest.HitTest(point);
        UiPointerActivation press = PressState;
        if (down)
        {
            press.Press(hit);
            return;
        }
        if (press.Release(hit) is { } action)
        {
            game.TryDispatchMenu(action);
        }
    }

    public void ResetPointer()
    {
        PressState.Reset();
        StatsDrag.Value = ScrollbarInteractionState.Default;
    }

    internal bool ScrollStats(UiInputEvent input)
    {
        if (game.Screen != TinyFarmScreen.Stats)
        {
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
        ScrollbarInteractionResult result = ScrollbarInteraction.Reduce(StatsDrag.Value,
            new ScrollbarInteractionContext(new ScrollbarInteractionTarget("agent-properties"), interaction,
                TinyFarmStatsScrollbar.ViewportHeight, WheelMultiplier: TinyFarmStatsScrollbar.RowHeight * 3), hit, input);
        StatsDrag.Value = result.State;
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

    public PointerPoint ActionCenter(TinyFarmFrame frame, UiAction action)
    {
        MachinaPreparedPresentation prepared = PrepareBase(frame);
        if (prepared.TryGetActionCenter(action, out PointerPoint point))
        {
            return point;
        }
        throw new ArgumentException($"No visible action '{action.Name}'.", nameof(action));
    }

    public bool TryActionCenter(TinyFarmFrame frame, string alias, out PointerPoint point)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            point = default;
            return false;
        }
        return PrepareBase(frame).TryGetActionCenter(new UiActionId(alias), out point);
    }

    public TinyFarmUiResources Resources(TinyFarmFrame frame)
    {
        return new TinyFarmUiResources(Resource(frame), ClockResource(frame), PromptResource(frame));
    }

    public MachinaPresentationFrame InspectorResource(TinyFarmPresentationLayout layout)
    {
        string viewportText = $"{layout.Width}x{layout.Height} | world {layout.WorldScale:0.0} px/m | HUD {game.Presentation.HudVisible}";
        var nodes = new List<UiNode>();
        nodes.Add(StandardUI.Panel(0, 0, 760, 184, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.Inspector], style: TinyFarmUiTheme.Panel(0x102D25EE)));
        nodes.Add(StandardUI.Label("OBLIVION / PRESENTATION / F10", 14, 10, 720,
            id: TinyFarmUiIds.Nodes[TinyFarmUiNode.InspectTitle], color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Label(viewportText, 14, 38, 720,
            id: TinyFarmUiIds.Nodes[TinyFarmUiNode.InspectViewport], color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Label(game.LiveInspection.Presentation?.Sampling ?? "No sampler facts", 14, 66, 730,
            id: TinyFarmUiIds.Nodes[TinyFarmUiNode.InspectSampling], color: TinyFarmUiTheme.Text));
        nodes.Add(StandardUI.Label("World projection independent of HUD / semantic state untouched", 14, 94, 730,
            id: TinyFarmUiIds.Nodes[TinyFarmUiNode.InspectAuthority], color: TinyFarmUiTheme.Text));
        string[] detail = (game.LiveInspection.Presentation?.Detail ?? "No native detail facts").Split("; ");
        for (int line = 0; line < detail.Length; line++)
        {
            nodes.Add(StandardUI.Label(detail[line], 14, 122 + line * 28, 730,
                id: TinyFarmUiIds.InspectorDetails[line], color: TinyFarmUiTheme.Text));
        }
        var node = UI.Surface(id: TinyFarmUiIds.Nodes[TinyFarmUiNode.PresentationInspector], width: 760, height: 184, children: nodes);
        return Cache(ViewLayer.Inspector).Prepare(node, 760, 184).PresentationFrame;
    }

    private MachinaPresentationFrame ClockResource(TinyFarmFrame frame)
    {
        var nodes = new List<UiNode>();
        nodes.Add(StandardUI.Label($"{frame.CurrentLocationName}  /  {frame.Time}", 0, 0, 400,
            id: TinyFarmUiIds.Nodes[TinyFarmUiNode.Clock], color: TinyFarmUiTheme.Text));
        return Cache(ViewLayer.Clock).Prepare(
            UI.Surface(id: TinyFarmUiIds.Nodes[TinyFarmUiNode.ClockSurface], width: 400, height: 34, children: nodes), 400, 34).PresentationFrame;
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
        var nodes = new List<UiNode>();
        int width = Math.Clamp(prompt.Length * 10 + 24, 100, 460);
        nodes.Add(StandardUI.Panel(0, 0, width, 32, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.Prompt], style: TinyFarmUiTheme.Panel(0x203D32EE)));
        nodes.Add(StandardUI.Label(prompt, 12, 6, width - 24,
            id: TinyFarmUiIds.Nodes[TinyFarmUiNode.PromptText], color: ColorToken.Hex(0xFFF0BEFF)));
        return Cache(ViewLayer.Prompt).Prepare(
            UI.Surface(id: TinyFarmUiIds.Nodes[TinyFarmUiNode.PromptSurface], width: 710, height: 38, children: nodes), 710, 38).PresentationFrame;
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
        nodes.Add(StandardUI.Panel(24, 72, 225, 40, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.Health], style: TinyFarmUiTheme.Panel(), children:
        [
            StandardUI.Label($"HP {health}/12  SP {player.Rpg?.SpiritCurrent ?? 0}/{player.Rpg?.SpiritMaximum ?? 0}",
                12, 10, 210, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.HealthText], color: TinyFarmUiTheme.Accent),
        ]));
        string tool = game.State.SelectedHotbarSlot switch
        {
            1 => "TOOL: SEEDS / 1 + K",
            3 => "TOOL: AXE / 3 + K",
            4 => "SWORD / J",
            _ => "TOOL: TURNIP / 2 + K"
        };
        nodes.Add(StandardUI.Label(tool, 36, 122, 210, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.SelectedTool],
            color: TinyFarmUiTheme.Accent));
        Journal(nodes);
        nodes.Add(StandardUI.Panel(24, 662, 910, 36, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.SliceStatus], style: TinyFarmUiTheme.Panel(), children:
        [
            StandardUI.Label(game.Status, 12, 10, 890, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.SliceStatusText],
                color: TinyFarmUiTheme.Text),
        ]));
        if (game.Dialogue.Presentation is { } dialogue)
        {
            // Keep the panel's bottom fixed; measured body and choices determine its height.
            string speaker = game.Dialogue.SpeakingActor is ActorId actor ? game.State.Actor(actor).Name : string.Empty;
            RectNode body = StandardUI.Paragraph(dialogue.Text, 790, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.DialogueBody],
                foreground: TinyFarmUiTheme.Text, measurer: textMeasurer);
            var children = new List<UiNode>
            {
                StandardUI.Label(speaker.ToUpperInvariant(), 28, 16, 780, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.Speaker],
                    color: TinyFarmUiTheme.Accent),
                StandardUI.Place(body, 28, 48),
            };
            double nextY = 48 + body.Height!.Value + 8;
            for (int index = 0; index < dialogue.Choices.Count; index++)
            {
                bool selected = dialogue.SelectedChoiceIndex == index;
                children.Add(StandardUI.Label((selected ? ">  " : "    ") + dialogue.Choices[index].Text,
                    46, nextY, 760, height: 28, id: TinyFarmUiIds.Choices[dialogue.Choices[index].Id],
                    color: ColorToken.Hex(selected ? 0xFFF0BEFFu : 0xCEDCCFFFu)));
                nextY += 28;
            }
            double panelHeight = Math.Max(172, nextY + 48);
            children.Add(StandardUI.Label("CLICK or SPACE next   /   UP-DOWN choose   /   ESC leave",
                28, panelHeight - 32, 790, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.DialogueHint], color: TinyFarmUiTheme.Text));
            nodes.Add(StandardUI.Panel(250, 622 - panelHeight, 850, panelHeight,
                id: TinyFarmUiIds.Nodes[TinyFarmUiNode.Dialogue], children: children, style: TinyFarmUiTheme.Panel(0x19362BF5)));
        }
        return UI.Surface(id: TinyFarmUiIds.Nodes[TinyFarmUiNode.OpeningSlice], width: 1280, height: 720, children: nodes);
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
            StandardUI.Label(game.JournalTitle, 18, 10, 310, id: TinyFarmUiIds.Nodes[TinyFarmUiNode.JournalTitle],
                color: TinyFarmUiTheme.Accent),
        };
        double nextY = 44;
        for (int index = 0; index < objectives.Length; index++)
        {
            RectNode paragraph = StandardUI.Paragraph(objectives[index], 316, id: TinyFarmUiIds.Objectives[index],
                variant: MachinaTextVariant.Caption, foreground: TinyFarmUiTheme.Text, measurer: textMeasurer);
            children.Add(StandardUI.Place(paragraph, 18, nextY));
            nextY += paragraph.Height!.Value + 6;
        }
        nodes.Add(StandardUI.Panel(912, 96, 344, nextY + 8,
            id: TinyFarmUiIds.Nodes[TinyFarmUiNode.Journal], children: children, style: TinyFarmUiTheme.Panel()));
    }

}

internal readonly record struct TinyFarmUiResources(
    MachinaPresentationFrame Base,
    MachinaPresentationFrame Clock,
    MachinaPresentationFrame? Prompt);
