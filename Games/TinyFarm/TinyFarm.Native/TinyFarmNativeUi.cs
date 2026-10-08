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
                InteractionTargetKind.Plot => game.State.Slice is not null ? SlicePlotPrompt(target) : "1 + SPACE plant   /   E tend or harvest",
                InteractionTargetKind.Enemy => game.State.Slice is not null ? "J sword   /   SPACE dodge" : "4 + SPACE  Shoo the slime",
                InteractionTargetKind.Tree => game.State.Slice is not null ? "3 + K  Chop firewood" : "3 + SPACE  Chop firewood",
                InteractionTargetKind.GroundItem => "E  Pick up " + game.State.Item(target.Item!.Value).Name,
                InteractionTargetKind.ForageNode => "E  Gather " + game.Definitions.Item(game.Definitions.ForageNode(target.ForageNode!.Value).Product).Name,
                InteractionTargetKind.Container => "E  Open " + game.State.Actor(target.Actor!.Value).Name,
                InteractionTargetKind.CookingStation => game.State.Version >= TinyFarmState.CraftingSaveVersion ? "E  Open stove" : game.State.Slice is not null ? "E  Cook turnip broth" : "E  Cook supper",
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

    private string SlicePlotPrompt(InteractionTarget target)
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
        if (game.State.Slice is not null && route.TargetScene != TinyFarmSceneIds.Farm
            && route.TargetScene != TinyFarmSceneIds.Residence && route.TargetScene != TinyFarmSceneIds.Overworld
            && route.TargetScene != TinyFarmSceneIds.DungeonEntrance)
        {
            return "Unfinished road / closed in this slice";
        }
        return "E  " + route.InteractionLabel;
    }

    private TinyFarmUiKey CreateKey(TinyFarmFrame frame)
    {
        int objectives = 0;
        if (game.State.Facts.Contains(WorldFact.SupperSeedPlanted))
        {
            objectives |= 1;
        }
        if (game.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.SauteedHenOfTheWoods) > 0)
        {
            objectives |= 2;
        }
        if (game.State.Enemy(TinyFarmIds.DungeonSlime).Lifecycle == EnemyLifecycle.Defeated)
        {
            objectives |= 4;
        }
        if (game.State.Items.FirstOrDefault(item => item.Id == TinyFarmIds.WildMint)?.Owner is not null)
        {
            objectives |= 8;
        }
        if (TinyFarmSupper.IsComplete(game.State))
        {
            objectives |= 16;
        }

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
        if (game.State.Slice is not null)
        {
            return BuildSlice(frame);
        }
        List<UiNode> nodes = [];
        Panel(nodes, "header", 22, 18, 1236, 74);
        Text(nodes, "title", "TINYFARM", 44, 29, 230, TextSize.H1, 0xEDD6A0FF);
        Text(nodes, "subtitle", "A LITTLE MINT OF KINDNESS", 245, 45, 510, TextSize.Md);

        Panel(nodes, "journal", 946, 110, 312, 457);
        Text(nodes, "journal-title", "SUPPER AT HOME", 965, 132, 275, TextSize.Md, 0xEDD6A0FF);
        int y = 185;
        foreach (string objective in game.Objectives())
        {
            Lines(nodes, "objective-" + y, objective, 967, y, 267, 22, TextSize.Md);
            y += 67;
        }
        ActorSceneState mara = game.State.ActorScene(TinyFarmIds.Mara);
        Text(nodes, "mara-location", "Mara: " + game.Definitions.Scenes.Get(mara.Scene).Name, 966, 535, 270, TextSize.Md, 0xEDD6A0FF);

        Panel(nodes, "footer", 22, 582, 1236, 120);
        string[] slots = ["1  SEEDS", "2  TURNIP", "3  AXE", "4  SWORD"];
        for (int index = 0; index < slots.Length; index++)
        {
            bool selected = game.State.SelectedHotbarSlot == index + 1;
            Panel(nodes, "slot-" + index, 42 + index * 155, 598, 145, 38, selected ? 0x52775EFFu : 0x263F38FFu);
            Text(nodes, "slot-label-" + index, slots[index], 54 + index * 155, 606, 127, TextSize.Md,
                selected ? 0xFFF0BEFF : 0xD8E3D6FF);
        }
        Text(nodes, "controls", "WASD move  E interact  SPACE tool  I bag", 690, 604, 540, TextSize.Md);
        Text(nodes, "save-controls", "F9 HUD F10 inspect F11 capture", 690, 634, 490, TextSize.Md, 0xEDD6A0FF);
        Text(nodes, "status", game.Status.Length > 97 ? game.Status[..94] + "..." : game.Status, 43, 668, 1180, TextSize.Md);

        if (game.Dialogue.Presentation is { } dialogue)
        {
            Panel(nodes, "dialogue", 54, 329, 1172, 241, 0x142F29FA);
            Text(nodes, "speaker", "MARA  /  a neighbour, and a very good cook", 82, 346, 1000, TextSize.Md, 0xEDD6A0FF);
            Lines(nodes, "dialogue-body", dialogue.Text, 82, 390, 1080, 96, TextSize.Md);
            for (int index = 0; index < dialogue.Choices.Count; index++)
            {
                bool selected = dialogue.SelectedChoiceIndex == index;
                Text(nodes, "choice-" + index, (selected ? ">  " : "    ") + dialogue.Choices[index].Text,
                    100, 449 + index * 31, 1040, TextSize.Md, selected ? 0xFFF0BEFF : 0xCEDCCFFF);
            }
            Text(nodes, "dialogue-hint", "SPACE / ENTER next     UP / DOWN choose     ESC leave     F save", 82, 531, 1060, TextSize.Md);
        }
        else if (game.Screen != TinyFarmScreen.Playing)
        {
            Panel(nodes, "modal", 193, 133, 894, 436, 0x163D31FC);
            string title = game.Screen switch
            {
                TinyFarmScreen.Title => "A LITTLE MINT OF KINDNESS",
                TinyFarmScreen.Complete => "SUPPER IS READY",
                TinyFarmScreen.Inventory => "YOUR POCKETS",
                _ => "TAKE A BREATHER"
            };
            Text(nodes, "modal-title", title, 233, 167, 800, TextSize.H1, 0xEDD6A0FF);
            string body = game.Screen switch
            {
                TinyFarmScreen.Title => "A seed for tomorrow. A meal for today.\nHelp Mara make a little corner of the world feel like home.\nPlant, forage, cook - and discourage one uninvited slime.\nA small afternoon adventure. No timer. No grinding.",
                TinyFarmScreen.Complete => "The stove is warm. The burrow is quiet.\nMara has set another place at the table: yours.\nYou finished this little afternoon. Thank you for playing.\nSave your home, or stay a little longer.",
                TinyFarmScreen.Inventory => string.Join('\n', frame.Inventory.Select(item => $"{item.Name}  x{item.Count}")),
                _ => "Your afternoon is paused.\nWASD move / face objects. E interacts.\n1 seeds, 3 axe, 4 sword. SPACE uses the selected tool.\nFollow doorway signs. The journal keeps track of supper."
            };
            int lineY = 232;
            foreach (string line in body.Split('\n'))
            {
                Text(nodes, "modal-line-" + lineY, line, 235, lineY, 800, TextSize.Md);
                lineY += 35;
            }
            Text(nodes, "modal-action", game.Screen == TinyFarmScreen.Title ? "ENTER  Begin your afternoon" : "ENTER  Back to the farm", 235, 460, 790, TextSize.Md, 0xEDD6A0FF);
            string secondary = game.Screen == TinyFarmScreen.Title
                ? "N  Continue saved game     Q  Quit"
                : "F  Save     N  Continue saved game     Q  Quit";
            Text(nodes, "modal-secondary", secondary, 235, 507, 790, TextSize.Md);
            if (game.Status.StartsWith("Could not", StringComparison.Ordinal))
            {
                Text(nodes, "modal-error", game.Status, 235, 538, 800, TextSize.Md, 0xFFD1A0FF);
            }
        }
        return UI.Surface(id: "supper", width: 1280, height: 720, children: nodes);
    }

    private UiNode BuildSlice(TinyFarmFrame frame)
    {
        var nodes = new List<UiNode>();
        TinyFarmSliceState slice = game.State.Slice!;
        Panel(nodes, "health", 24, 72, 225, 40, 0x19362BD9);
        Text(nodes, "health-text", $"HP {slice.Health}/12  SP {game.State.Actor(TinyFarmIds.Player).Rpg?.SpiritCurrent ?? 0}/{game.State.Actor(TinyFarmIds.Player).Rpg?.SpiritMaximum ?? 0}",
            36, 82, 210, TextSize.Md, 0xF5DDB5FF);
        string tool = game.State.SelectedHotbarSlot switch
        {
            1 => "TOOL: SEEDS / 1 + K",
            3 => "TOOL: AXE / 3 + K",
            4 => "SWORD / J",
            _ => "TOOL: TURNIP / 2 + K"
        };
        Text(nodes, "selected-tool", tool, 36, 122, 210, TextSize.Md, 0xF5DDB5FF);
        Panel(nodes, "slice-status", 24, 662, 910, 36, 0x19362BD9);
        Text(nodes, "slice-status-text", game.Status, 36, 672, 890, TextSize.Md);
        if (game.Dialogue.Presentation is { } dialogue)
        {
            Panel(nodes, "dialogue", 180, 450, 920, 172, 0x19362BF5);
            Text(nodes, "speaker", "MARA", 208, 466, 850, TextSize.Md, 0xF5DDB5FF);
            Lines(nodes, "dialogue-body", dialogue.Text, 208, 498, 850, 83, TextSize.Md);
            Text(nodes, "dialogue-hint", "ENTER next   /   ESC leave", 208, 590, 850, TextSize.Md);
        }
        else if (game.Screen != TinyFarmScreen.Playing)
        {
            Panel(nodes, "modal", 220, 130, 840, 460, 0x19362BFA);
            string title = game.Screen switch
            {
                TinyFarmScreen.Title => "TINYFARM / THE SLEEPING SPRING",
                TinyFarmScreen.Inventory => "YOUR POCKETS & PLANS",
                _ => "A MOMENT AT HOME"
            };
            Text(nodes, "modal-title", title, 252, 165, 776, TextSize.H1, 0xF5DDB5FF);
            string[] lines = game.Screen == TinyFarmScreen.Inventory
                ? new[]
                {
                    $"Seeds {game.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.TurnipSeed)} / Turnips {game.State.ProductCount(TinyFarmIds.Player, TinyFarmIds.Turnip)} / Broth {game.State.ProductCount(TinyFarmIds.Player, new ProductId("turnip-broth"))}",
                    "Tools: J sword / 1 + K seeds / 3 + K axe"
                }.Concat(game.Objectives()).ToArray()
                : ["Make a little home beside the woods.",
                   "Harvest the ripe turnip. Cook broth at your house stove.",
                   "Follow the east path to Old Burrow. Watch the slime before striking.",
                   "Plant and water. Sleep in your bed. Return to your own harvest.",
                   "WASD move   E interact   J sword   SPACE dodge",
                   "1 + K plant   R eat broth   I pockets & plans",
                   "F save   N continue   F9 HUD   F11 clean capture"];
            for (int row = 0; row < lines.Length; row++)
            {
                Text(nodes, "modal-row-" + row, lines[row], 252, 231 + row * 34, 776, TextSize.Md);
            }
            Text(nodes, "modal-action", "ENTER begin / return    Q quit", 252, 550, 776, TextSize.Md, 0xF5DDB5FF);
        }
        return UI.Surface(id: "opening-slice", width: 1280, height: 720, children: nodes);
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

    private static void Lines(List<UiNode> nodes, string id, string text, int x, int y, int width, int characters, TextSize size)
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
