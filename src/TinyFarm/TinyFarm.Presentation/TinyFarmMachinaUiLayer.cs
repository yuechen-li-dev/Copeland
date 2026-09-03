using Aurelian.Composition;
using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Pipeline;
using Machina.Runtime.Input;
using Machina.Layout.Rows;
using System.Diagnostics;
using TinyFarm.Core;

namespace TinyFarm.Presentation;

public sealed class TinyFarmMachinaUiLayer : IAurelianLayer, IAurelianLayerMessageReceiver<TinyFarmPresentationSnapshot>
{
    public static readonly LayerId Id = new("tiny-farm-ui");
    public static readonly LayerId ApplicationId = new("tiny-farm-application");

    private readonly ILayerApplicationMessageSink messageSink;
    private readonly MachinaPresentationPipeline pipeline = new();
    private LayerSurfaceDescriptor surface;
    private TinyFarmPresentationSnapshot? snapshot;
    private MachinaPreparedPresentation? prepared;
    private TinyFarmPresentationSnapshot? preparedSnapshot;

    public TinyFarmMachinaUiLayer(ILayerApplicationMessageSink messageSink, LayerSurfaceDescriptor surface)
    {
        this.messageSink = messageSink ?? throw new ArgumentNullException(nameof(messageSink));
        this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
    }

    public MachinaPreparedPresentation Prepared => prepared
        ?? throw new InvalidOperationException("The TinyFarm UI layer has not prepared a presentation.");

    public double LastRecompositionMicroseconds { get; private set; }

    public TinyFarmMachinaCacheMetrics CacheMetrics { get; } = new();

    public LayerDescriptor Describe()
    {
        return new LayerDescriptor(
            Id,
            100,
            true,
            surface.FullViewport,
            LayerPresentationMode.DirectHostPass,
            snapshot?.InventoryOpen == true ? LayerInputPolicy.Opaque : LayerInputPolicy.HitTest);
    }

    public void Attach(LayerSurfaceDescriptor attachedSurface)
    {
        bool surfaceChanged = !SurfaceEquals(surface, attachedSurface);
        surface = attachedSurface;
        Recompose(surfaceChanged ? TinyFarmUiInvalidation.Layout : TinyFarmUiInvalidation.None);
    }

    public void Resize(LayerSurfaceDescriptor resizedSurface)
    {
        bool surfaceChanged = !SurfaceEquals(surface, resizedSurface);
        surface = resizedSurface;
        Recompose(surfaceChanged ? TinyFarmUiInvalidation.Layout : TinyFarmUiInvalidation.None);
    }

    public void Update(LayerUpdateContext context)
    {
    }

    public LayerPresentationDto Present(LayerPresentationContext context)
    {
        if (prepared is null)
        {
            Recompose(TinyFarmUiInvalidation.Topology);
        }
        return new LayerPresentationDto(Id, surface.FullViewport, true, surface.Kind, "machina-presentation-frame");
    }

    public LayerInputResult HandleInput(LayerInputEvent input)
    {
        if (snapshot is null || prepared is null)
        {
            return LayerInputResult.Unconsumed;
        }

        if (input is LayerPointerButtonChanged { Button: LayerPointerButton.Primary, IsPressed: true } pointer)
        {
            UiHitTestResult? hit = prepared.HitTest.HitTest(new PointerPoint(pointer.Position.X, pointer.Position.Y));
            if (hit is null)
            {
                return snapshot.InventoryOpen
                    ? new LayerInputResult(true, RequestFocus: true)
                    : LayerInputResult.Unconsumed;
            }
            Publish(DecodeAction(hit.Action.Id));
            return new LayerInputResult(true, RequestFocus: true, RequestCapture: true);
        }

        if (input is LayerPointerButtonChanged { Button: LayerPointerButton.Primary, IsPressed: false })
        {
            return new LayerInputResult(snapshot.InventoryOpen, ReleaseCapture: true);
        }

        if (input is LayerKeyChanged { IsPressed: true, IsRepeat: false } key)
        {
            if (snapshot.InventoryOpen && IsWorldKey(key.Key))
            {
                return new LayerInputResult(true, RequestFocus: true);
            }
            TinyFarmUiCommandDto? command = DecodeKey(key.Key);
            if (command is not null)
            {
                Publish(command);
                return new LayerInputResult(true, RequestFocus: snapshot.InventoryOpen);
            }
        }

        return LayerInputResult.Unconsumed;
    }

    public void Detach()
    {
        prepared = null;
        preparedSnapshot = null;
    }

    public void Receive(LayerMessage<TinyFarmPresentationSnapshot> message)
    {
        snapshot = message.Payload ?? throw new ArgumentNullException(nameof(message));
        Recompose(TinyFarmUiInvalidation.None);
    }

    private void Recompose(TinyFarmUiInvalidation requestedInvalidation)
    {
        if (snapshot is null)
        {
            return;
        }

        long started = Stopwatch.GetTimestamp();
        TinyFarmUiInvalidation invalidation = ResolveInvalidation(requestedInvalidation);
        switch (invalidation)
        {
            case TinyFarmUiInvalidation.None:
                break;
            case TinyFarmUiInvalidation.Value:
                prepared = MachinaPreparedPresentationUpdater.ApplyValues(
                    Prepared,
                    TinyFarmMachinaView.BuildValuePatch(snapshot, surface.Width, surface.Height));
                CacheMetrics.RecordDynamicUpdate();
                break;
            case TinyFarmUiInvalidation.Layout:
                prepared = PrepareFull(snapshot);
                CacheMetrics.RecordLayoutBuild();
                CacheMetrics.RecordPresentationLower();
                CacheMetrics.RecordHitTestBuild();
                break;
            case TinyFarmUiInvalidation.Topology:
                prepared = PrepareFull(snapshot);
                CacheMetrics.RecordTopologyBuild();
                CacheMetrics.RecordLayoutBuild();
                CacheMetrics.RecordPresentationLower();
                CacheMetrics.RecordHitTestBuild();
                break;
            default:
                throw new InvalidOperationException($"Unknown TinyFarm UI invalidation '{invalidation}'.");
        }
        preparedSnapshot = snapshot;
        LastRecompositionMicroseconds = Stopwatch.GetElapsedTime(started).TotalMicroseconds;
    }

    private MachinaPreparedPresentation PrepareFull(TinyFarmPresentationSnapshot current)
    {
        UiNode document = TinyFarmMachinaView.Build(current, surface.Width, surface.Height);
        return pipeline.Prepare(document, surface.Width, surface.Height);
    }

    private TinyFarmUiInvalidation ResolveInvalidation(TinyFarmUiInvalidation requested)
    {
        if (prepared is null || preparedSnapshot is null)
        {
            return TinyFarmUiInvalidation.Topology;
        }
        if (requested == TinyFarmUiInvalidation.Layout)
        {
            return TinyFarmUiInvalidation.Layout;
        }
        if (!TinyFarmMachinaView.HasSameTopology(preparedSnapshot, snapshot!))
        {
            return TinyFarmUiInvalidation.Topology;
        }
        return TinyFarmMachinaView.HasSameValues(preparedSnapshot, snapshot!)
            ? TinyFarmUiInvalidation.None
            : TinyFarmUiInvalidation.Value;
    }

    private static bool SurfaceEquals(LayerSurfaceDescriptor left, LayerSurfaceDescriptor right)
    {
        return left.Width == right.Width
            && left.Height == right.Height
            && left.Scale.Equals(right.Scale)
            && left.Kind == right.Kind;
    }

    private void Publish(TinyFarmUiCommandDto command)
    {
        messageSink.Publish(new LayerMessage<TinyFarmUiCommandDto>(Id, ApplicationId, command));
    }

    private static TinyFarmUiCommandDto DecodeAction(UiActionId action)
    {
        const string slotPrefix = "tiny-farm.hotbar.";
        if (action.Value.StartsWith(slotPrefix, StringComparison.Ordinal)
            && int.TryParse(action.Value.AsSpan(slotPrefix.Length), out int slot))
        {
            return new TinyFarmUiCommandDto(TinyFarmUiCommandKind.SelectHotbarSlot, slot);
        }
        return action.Value switch
        {
            "tiny-farm.inventory.toggle" => new TinyFarmUiCommandDto(TinyFarmUiCommandKind.ToggleInventory),
            "tiny-farm.simulation.pause-play" => new TinyFarmUiCommandDto(TinyFarmUiCommandKind.TogglePausePlay),
            "tiny-farm.simulation.fast-forward" => new TinyFarmUiCommandDto(TinyFarmUiCommandKind.ToggleFastForward),
            _ => throw new InvalidOperationException($"Unknown TinyFarm Machina action '{action.Value}'.")
        };
    }

    private static TinyFarmUiCommandDto? DecodeKey(LayerKey key)
    {
        if (key is >= LayerKey.Number1 and <= LayerKey.Number8)
        {
            return new TinyFarmUiCommandDto(
                TinyFarmUiCommandKind.SelectHotbarSlot,
                (int)key - (int)LayerKey.Number1 + 1);
        }
        return key switch
        {
            LayerKey.I => new TinyFarmUiCommandDto(TinyFarmUiCommandKind.ToggleInventory),
            LayerKey.Space => new TinyFarmUiCommandDto(TinyFarmUiCommandKind.TogglePausePlay),
            LayerKey.F => new TinyFarmUiCommandDto(TinyFarmUiCommandKind.ToggleFastForward),
            LayerKey.N => new TinyFarmUiCommandDto(TinyFarmUiCommandKind.Wait),
            LayerKey.Q => new TinyFarmUiCommandDto(TinyFarmUiCommandKind.UseSelected),
            LayerKey.Enter => new TinyFarmUiCommandDto(TinyFarmUiCommandKind.Interact),
            _ => null
        };
    }

    private static bool IsWorldKey(LayerKey key)
    {
        return key is LayerKey.ArrowLeft or LayerKey.ArrowRight or LayerKey.ArrowUp or LayerKey.ArrowDown
            or LayerKey.Enter or LayerKey.Q or LayerKey.N;
    }
}

internal enum TinyFarmUiInvalidation
{
    None,
    Value,
    Layout,
    Topology
}

public sealed class TinyFarmMachinaCacheMetrics
{
    public int TopologyBuildCount { get; private set; }

    public int LayoutBuildCount { get; private set; }

    public int PresentationLowerCount { get; private set; }

    public int HitTestBuildCount { get; private set; }

    public int DynamicUpdateCount { get; private set; }

    internal void RecordTopologyBuild() => TopologyBuildCount++;

    internal void RecordLayoutBuild() => LayoutBuildCount++;

    internal void RecordPresentationLower() => PresentationLowerCount++;

    internal void RecordHitTestBuild() => HitTestBuildCount++;

    internal void RecordDynamicUpdate() => DynamicUpdateCount++;
}

public static class TinyFarmMachinaView
{
    private static readonly ColorToken HudBackground = ColorToken.Hex(0x131B19FF);
    private static readonly ColorToken PanelBackground = ColorToken.Hex(0x121B19F2);
    private static readonly ColorToken Available = ColorToken.Hex(0x31483FFF);
    private static readonly ColorToken Unavailable = ColorToken.Hex(0x303030FF);
    private static readonly ColorToken Empty = ColorToken.Hex(0x1E2523FF);
    private static readonly ColorToken Border = ColorToken.Hex(0x7E9185FF);
    private static readonly ColorToken OldGold = ColorToken.Hex(0xFFD700FF);
    private static readonly ColorToken PanelBorder = ColorToken.Hex(0xB59A53FF);
    private static readonly ColorToken Hint = ColorToken.Hex(0xF2CD6FFF);
    private static readonly ColorToken InventoryOpen = ColorToken.Hex(0x5B4926FF);
    private static readonly ColorToken InventoryClosed = ColorToken.Hex(0x263531FF);

    public static UiNode Build(TinyFarmPresentationSnapshot snapshot, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        TinyFarmPlayerUiLayout layout = TinyFarmPlayerUiLayoutEngine.Compute(width, height, snapshot.PlayerUi.Inventory.Count);
        int hudHeight = Math.Clamp(height / 12, 76, 112);
        int hudTop = height - hudHeight;
        var children = new List<UiNode>();

        for (int index = 0; index < snapshot.PlayerUi.Hotbar.Count; index++)
        {
            TinyFarmHotbarSlotView slot = snapshot.PlayerUi.Hotbar[index];
            TinyFarmUiRectangle rectangle = layout.HotbarSlots[index];
            string label = HotbarLabel(slot);
            UiNode hotbar = UI.Rect(
                UI.Layer(
                    id: $"tiny-farm.hotbar.content.{slot.Slot.Value}",
                    children:
                    [
                        PlaceText($"tiny-farm.hotbar.number.{slot.Slot.Value}", slot.Slot.Value.ToString(), 7, 6, 24, 12, ColorToken.White),
                        PlaceText($"tiny-farm.hotbar.label.{slot.Slot.Value}", label, 7, 24, rectangle.Width - 14, 12, ColorToken.White)
                    ]),
                id: $"tiny-farm.hotbar.button.{slot.Slot.Value}",
                style: HotbarStyle(slot)) with
            {
                DeclaredAction = UiAction.Named($"tiny-farm.hotbar.{slot.Slot.Value}"),
                Semantics = new UiSemantics(UiRole.Button, label, Focusable: true)
            };
            children.Add(UI.At(
                hotbar,
                id: $"tiny-farm.hotbar.anchor.{slot.Slot.Value}",
                x: rectangle.X,
                y: rectangle.Y,
                width: rectangle.Width,
                height: rectangle.Height));
        }

        if (snapshot.InventoryOpen)
        {
            TinyFarmUiRectangle panel = layout.InventoryPanel;
            var inventoryChildren = new List<UiNode>
            {
                PlaceText("tiny-farm.inventory.title", "INVENTORY", 18, 16, panel.Width - 36, 24, OldGold, TextSize.H1)
            };
            if (snapshot.PlayerUi.Inventory.Count == 0)
            {
                inventoryChildren.Add(PlaceText(
                    "tiny-farm.inventory.empty", "EMPTY", 18, 50, panel.Width - 36, 12, ColorToken.White));
            }
            else
            {
                for (int index = 0; index < snapshot.PlayerUi.Inventory.Count; index++)
                {
                    TinyFarmPlayerInventoryView item = snapshot.PlayerUi.Inventory[index];
                    inventoryChildren.Add(PlaceText(
                        InventoryRowId(item.SemanticId),
                        $"{item.Name.ToUpperInvariant()}  X{item.Count}",
                        18,
                        52 + (index * 30),
                        panel.Width - 36,
                        12,
                        ColorToken.White));
                }
            }
            children.Add(UI.At(
                UI.Rect(
                    UI.Layer(id: "tiny-farm.inventory.content", children: inventoryChildren),
                    id: "tiny-farm.inventory.panel",
                    color: PanelBackground,
                    borderColor: PanelBorder,
                    borderThickness: 3),
                id: "tiny-farm.inventory.panel.anchor",
                x: panel.X,
                y: panel.Y,
                width: panel.Width,
                height: panel.Height));
        }

        var hudChildren = new List<UiNode>
        {
            PlaceText(
                "tiny-farm.hud.heading",
                BuildHeading(snapshot),
                18,
                10,
                width - 36,
                height >= 900 ? 24 : 12,
                ColorToken.White,
                height >= 900 ? TextSize.H1 : TextSize.Md),
            PlaceText(
                "tiny-farm.hud.controls",
                BuildControls(snapshot),
                18,
                38,
                width - 36,
                12,
                Hint)
        };
        if (hudHeight >= 96)
        {
            hudChildren.Add(PlaceText(
                "tiny-farm.hud.message",
                (snapshot.Narrative.LastOrDefault() ?? snapshot.Status).ToUpperInvariant(),
                18,
                62,
                width - 36,
                12,
                ColorToken.White));
        }
        children.Add(UI.At(
            UI.Rect(
                UI.Layer(id: "tiny-farm.hud.content", children: hudChildren),
                id: "tiny-farm.hud.background",
                color: HudBackground),
            id: "tiny-farm.hud.anchor",
            x: 0,
            y: hudTop,
            width: width,
            height: hudHeight));

        UiNode inventoryToggle = UI.Rect(
            UI.Layer(
                id: "tiny-farm.inventory.button.content",
                children:
                [
                    PlaceText("tiny-farm.inventory.button.text", "I INVENTORY", 10, 10, 120, 12, ColorToken.White)
                ]),
            id: "tiny-farm.inventory.button",
            style: InventoryButtonStyle(snapshot.InventoryOpen)) with
        {
            DeclaredAction = UiAction.Named("tiny-farm.inventory.toggle"),
            Semantics = new UiSemantics(UiRole.Button, "Inventory", Focusable: true)
        };
        children.Add(UI.At(
            inventoryToggle,
            id: "tiny-farm.inventory.button.anchor",
            x: width - 158,
            y: hudTop + 8,
            width: 140,
            height: 32));

        return UI.Surface(id: "tiny-farm.ui.surface", width: width, height: height, children: children);
    }

    public static MachinaPresentationValuePatch BuildValuePatch(
        TinyFarmPresentationSnapshot snapshot,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var patch = new MachinaPresentationValuePatch();

        foreach (TinyFarmHotbarSlotView slot in snapshot.PlayerUi.Hotbar)
        {
            string label = HotbarLabel(slot);
            patch.SetText(new NodeId($"tiny-farm.hotbar.label.{slot.Slot.Value}.text"), label);
            patch.SetStyle(
                new NodeId($"tiny-farm.hotbar.button.{slot.Slot.Value}"),
                HotbarStyle(slot));
            patch.SetSemantics(
                new NodeId($"tiny-farm.hotbar.button.{slot.Slot.Value}"),
                new UiSemantics(UiRole.Button, label, Focusable: true));
        }

        if (snapshot.InventoryOpen)
        {
            if (snapshot.PlayerUi.Inventory.Count == 0)
            {
                patch.SetText(new NodeId("tiny-farm.inventory.empty.text"), "EMPTY");
            }
            else
            {
                foreach (TinyFarmPlayerInventoryView item in snapshot.PlayerUi.Inventory)
                {
                    patch.SetText(
                        new NodeId($"{InventoryRowId(item.SemanticId)}.text"),
                        $"{item.Name.ToUpperInvariant()}  X{item.Count}");
                }
            }
        }

        patch.SetText(new NodeId("tiny-farm.hud.heading.text"), BuildHeading(snapshot));
        patch.SetText(new NodeId("tiny-farm.hud.controls.text"), BuildControls(snapshot));
        if (Math.Clamp(height / 12, 76, 112) >= 96)
        {
            patch.SetText(
                new NodeId("tiny-farm.hud.message.text"),
                (snapshot.Narrative.LastOrDefault() ?? snapshot.Status).ToUpperInvariant());
        }
        patch.SetStyle(new NodeId("tiny-farm.inventory.button"), InventoryButtonStyle(snapshot.InventoryOpen));
        return patch;
    }

    public static bool HasSameTopology(
        TinyFarmPresentationSnapshot previous,
        TinyFarmPresentationSnapshot current)
    {
        if (previous.InventoryOpen != current.InventoryOpen)
        {
            return false;
        }
        if (previous.PlayerUi.Hotbar.Count != current.PlayerUi.Hotbar.Count)
        {
            return false;
        }
        for (int index = 0; index < current.PlayerUi.Hotbar.Count; index++)
        {
            if (previous.PlayerUi.Hotbar[index].Slot != current.PlayerUi.Hotbar[index].Slot)
            {
                return false;
            }
        }
        if (!current.InventoryOpen)
        {
            return true;
        }
        if (previous.PlayerUi.Inventory.Count != current.PlayerUi.Inventory.Count)
        {
            return false;
        }
        for (int index = 0; index < current.PlayerUi.Inventory.Count; index++)
        {
            if (!string.Equals(
                previous.PlayerUi.Inventory[index].SemanticId,
                current.PlayerUi.Inventory[index].SemanticId,
                StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    public static bool HasSameValues(
        TinyFarmPresentationSnapshot previous,
        TinyFarmPresentationSnapshot current)
    {
        if (ReferenceEquals(previous, current))
        {
            return true;
        }
        if (previous.Day != current.Day
            || previous.SimulationMode != current.SimulationMode
            || previous.InventoryOpen != current.InventoryOpen
            || previous.PlayerUi.Money != current.PlayerUi.Money
            || !string.Equals(previous.Time, current.Time, StringComparison.Ordinal)
            || !string.Equals(previous.LocationName, current.LocationName, StringComparison.Ordinal)
            || !string.Equals(previous.Status, current.Status, StringComparison.Ordinal)
            || !SequenceEqual(previous.InteractionHints, current.InteractionHints)
            || !SequenceEqual(previous.Narrative, current.Narrative)
            || previous.PlayerUi.Inventory.Count != current.PlayerUi.Inventory.Count
            || previous.PlayerUi.Hotbar.Count != current.PlayerUi.Hotbar.Count)
        {
            return false;
        }

        for (int index = 0; index < current.PlayerUi.Inventory.Count; index++)
        {
            if (previous.PlayerUi.Inventory[index] != current.PlayerUi.Inventory[index])
            {
                return false;
            }
        }
        for (int index = 0; index < current.PlayerUi.Hotbar.Count; index++)
        {
            if (previous.PlayerUi.Hotbar[index] != current.PlayerUi.Hotbar[index])
            {
                return false;
            }
        }
        return true;
    }

    private static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }
        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private static string InventoryRowId(string semanticId)
    {
        return $"tiny-farm.inventory.row.{semanticId}";
    }

    private static string HotbarLabel(TinyFarmHotbarSlotView slot)
    {
        return slot.BindingKind is null
            ? "EMPTY"
            : $"{slot.Label.ToUpperInvariant()} X{slot.Count}";
    }

    private static UiStyle HotbarStyle(TinyFarmHotbarSlotView slot)
    {
        ColorToken background = slot.VisualState switch
        {
            TinyFarmHotbarSlotVisualState.Available => Available,
            TinyFarmHotbarSlotVisualState.Unavailable => Unavailable,
            _ => Empty
        };
        return new UiStyle(
            Background: background,
            Foreground: ColorToken.White,
            BorderColor: slot.IsSelected ? OldGold : Border,
            BorderThickness: slot.IsSelected ? 4 : 2);
    }

    private static UiStyle InventoryButtonStyle(bool inventoryOpen)
    {
        return new UiStyle(
            Background: inventoryOpen ? InventoryOpen : InventoryClosed,
            Foreground: ColorToken.White,
            BorderColor: inventoryOpen ? OldGold : Border,
            BorderThickness: 2);
    }

    private static UiNode PlaceText(
        string id,
        string text,
        double x,
        double y,
        double width,
        double height,
        ColorToken color,
        TextSize size = TextSize.Md)
    {
        return UI.At(
            UI.Text(text, id: $"{id}.text", color: color, size: size),
            id: id,
            x: x,
            y: y,
            width: width,
            height: height);
    }

    private static string BuildHeading(TinyFarmPresentationSnapshot snapshot)
    {
        string mode = snapshot.SimulationMode switch
        {
            TinyFarmSimulationMode.Paused => "PAUSED",
            TinyFarmSimulationMode.Playing => "PLAY",
            _ => "FAST X10"
        };
        return $"{mode}  DAY {snapshot.Day}  {snapshot.Time}  {snapshot.LocationName.ToUpperInvariant()}  {snapshot.PlayerUi.Money}G";
    }

    private static string BuildControls(TinyFarmPresentationSnapshot snapshot)
    {
        string controls = "1-8 HOTBAR  |  I INVENTORY  |  SPACE PAUSE/PLAY  |  F FAST X10  |  ARROWS/WASD MOVE  |  ENTER/E INTERACT  |  Q USE  |  N WAIT  |  F5 SAVE  |  F9 LOAD";
        string context = string.Join("  |  ", snapshot.InteractionHints.Skip(4));
        if (snapshot.Narrative.Count > 0)
        {
            context = context.Length == 0 ? "ENTER CLOSE" : context + "  |  ENTER CLOSE";
        }
        string composed = context.Length == 0
            ? controls
            : controls + "  |  " + context;
        return composed.ToUpperInvariant();
    }
}
