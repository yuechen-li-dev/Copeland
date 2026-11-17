using System.Collections.Immutable;
using Machina.Core.Actions;
using Machina.Core.Lowering;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Runtime.Input;
using Machina.Runtime.State;
using Machina.Standard.Theme;

namespace Machina.Standard.Widgets;

/// <summary>
/// One window's transient interaction state. Values are controlled by the caller:
/// changes are returned as typed proposals, never written into an application model.
/// Use only on the owning UI thread. Suspend on focus/capture loss; dispose with the window.
/// </summary>
public sealed partial class WidgetSession : IDisposable
{
    private readonly UiStateScope lifetime = new();
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private IReadOnlyDictionary<NodeId, WidgetBinding> bindings = new Dictionary<NodeId, WidgetBinding>();
    private IReadOnlyDictionary<NodeId, UiAction> actions = new Dictionary<NodeId, UiAction>();
    private IReadOnlyDictionary<NodeId, Rect> rectangles = new Dictionary<NodeId, Rect>();
    private List<NodeId> focusOrder = [];
    private List<WidgetPopup> popups = [];
    private UiHitTestIndex? hitTest;
    private readonly List<WidgetChange> pending = [];
    private readonly Dictionary<NodeId, NodeId?> returnFocus = [];
    private readonly HashSet<NodeId> dismissedTooltips = [];
    private readonly Dictionary<NodeId, HashSet<NodeId>> popupChildren = [];
    private readonly HashSet<NodeId> overlayNodes = [];

    internal bool TooltipVisible(NodeId id, NodeId trigger)
    {
        bool active = State.Hover == trigger || State.Focus == trigger;
        if (!active) dismissedTooltips.Remove(id);
        return active && !dismissedTooltips.Contains(id);
    }

    private sealed class InputState
    {
        public NodeId? Focus;
        public NodeId? Hover;
        public bool FocusVisible;
        public NodeId? Pressed;
        public WidgetBinding? Drag;
        public decimal DragStart;
        public decimal DragValue;
        public NodeId? SpaceArmed;
        public TextSelection? Text;
        public bool SelectingText;
        public string Search = "";
        public TimeSpan SearchTime;
        public (NodeId Id, PointerPoint Point, TimeSpan Time, int Count)? LastClick;
    }

    private InputState State => lifetime.GetOrCreate(new NodeId("machina.widgets.input"),
        static () => new InputState(), UiStateLifetime.Activation);

    public NodeId? Focused => State.Focus;

    /// <summary>The strongest keyboard scope in effect; see <see cref="WidgetKeyboardScope"/>.</summary>
    public WidgetKeyboardScope KeyboardScope =>
        popups.Any(popup => popup.Modal) ? WidgetKeyboardScope.Modal
        : State.Focus is { } id && bindings.TryGetValue(id, out var binding) && binding.Kind is WidgetKind.Input or WidgetKind.Number
            ? WidgetKeyboardScope.TextEntry
        : popups.Any(popup => popup.Kind != WidgetPopupKind.Tooltip) ? WidgetKeyboardScope.Popup
        : State.Focus is not null ? WidgetKeyboardScope.Control : WidgetKeyboardScope.None;
    public NodeId? Hovered => State.Hover;
    public bool FocusVisible => State.FocusVisible;
    public bool HasPointerCapture => State.Drag is not null || State.Pressed is not null;
    public IWidgetClipboard? Clipboard { get; init; }

    public WidgetBuilder CreateBuilder(StandardTheme? theme = null, WidgetStyle? style = null)
    {
        EnsureOwner();
        return new WidgetBuilder(this, theme ?? StandardTheme.Default, style);
    }

    /// <summary>Called by the Machina widget pipeline after final layout, including overlays.</summary>
    public void Present(WidgetBuilder builder, UiLoweringResult lowering, ResolvedLayoutDocument resolved)
    {
        EnsureOwner();
        if (!ReferenceEquals(builder.Session, this)) throw new ArgumentException("Builder belongs to another session.", nameof(builder));
        var oldPopups = popups;
        dismissedTooltips.RemoveWhere(id => !builder.TooltipIds.Contains(id));
        bindings = builder.Bindings.Where(pair => resolved.Nodes.ContainsKey(pair.Key)).ToDictionary();
        rectangles = resolved.Nodes.ToDictionary(pair => pair.Key, pair => pair.Value.Rect);
        var mergedActions = new Dictionary<NodeId, UiAction>(lowering.Actions);
        // Disabled controls still occlude controls underneath them. Keep their true
        // disabled semantics; input routing consumes the hit without activating it.
        foreach (var binding in bindings.Values) mergedActions[binding.Id] = binding.Intent;
        actions = mergedActions;
        hitTest = UiHitTestIndex.Build(resolved, mergedActions, lowering.Semantics, lowering.Styles);
        var ordered = PreOrder(resolved).ToList();
        // Declaration evaluates child arguments before parents. Overlay tree
        // order, rather than declaration order, defines stacking and modality.
        popups = builder.Popups.Where(popup => resolved.Nodes.ContainsKey(popup.Panel))
            .OrderBy(popup => ordered.IndexOf(popup.Panel)).ToList();
        popupChildren.Clear();
        overlayNodes.Clear();
        foreach (var popup in popups)
        {
            popupChildren[popup.Id] = Descendants(resolved, popup.Panel);
            var overlay = popup.Id.Child("overlay");
            overlayNodes.UnionWith(resolved.Nodes.ContainsKey(overlay) ? Descendants(resolved, overlay) : popupChildren[popup.Id]);
        }
        var modal = popups.LastOrDefault(popup => popup.Modal);
        HashSet<NodeId>? modalNodes = modal is null ? null : Descendants(resolved, modal.Panel);
        if (modal is not null)
        {
            foreach (var popup in popups.Skip(popups.IndexOf(modal) + 1))
                if (modalNodes!.Contains(popup.Trigger)) modalNodes.UnionWith(popupChildren[popup.Id]);
        }
        focusOrder = ordered.Where(id => lowering.Semantics.TryGetValue(id, out var semantic)
            && semantic.Focusable && !semantic.Disabled && rectangles[id].Width > 0 && rectangles[id].Height > 0
            && (modalNodes is null || modalNodes.Contains(id))).ToList();

        foreach (var closed in oldPopups.Where(old => popups.All(current => current.Id != old.Id)))
        {
            if (returnFocus.Remove(closed.Id, out var previous)
                && previous is { } node && focusOrder.Contains(node)
                && (State.Focus is null || !focusOrder.Contains(State.Focus.Value))) State.Focus = node;
        }
        foreach (var opened in popups.Where(current => oldPopups.All(old => old.Id != current.Id)))
        {
            returnFocus[opened.Id] = opened.Kind == WidgetPopupKind.Combobox ? opened.Trigger : State.Focus ?? opened.Trigger;
            if (opened.Kind == WidgetPopupKind.Combobox) State.Focus = opened.Trigger;
            if (opened.Kind is WidgetPopupKind.Select or WidgetPopupKind.Menu or WidgetPopupKind.Popover || opened.Modal)
            {
                var children = Descendants(resolved, opened.Panel);
                State.Focus = focusOrder.FirstOrDefault(id => children.Contains(id)
                    && bindings.TryGetValue(id, out var binding) && binding.Selected.Contains(binding.Key ?? ""));
                if (State.Focus is null || !focusOrder.Contains(State.Focus.Value))
                    State.Focus = focusOrder.Where(children.Contains).Select(id => (NodeId?)id).FirstOrDefault();
                // A popover without focusable content keeps focus on its trigger.
                if (opened.Kind == WidgetPopupKind.Popover && State.Focus is null) State.Focus = returnFocus[opened.Id];
                State.FocusVisible = true;
            }
        }
        if (State.Focus is { } focused && !focusOrder.Contains(focused)) State.Focus = null;
        if (State.Drag is { } drag && (!bindings.TryGetValue(drag.Id, out var current) || current.Disabled || current.ReadOnly)) CancelCapture();
        if (State.Pressed is { } pressed && !bindings.ContainsKey(pressed)) State.Pressed = null;
        SynchronizeText();
        textMeasurer = builder.TextMeasurer;
        textStyle = builder.Style;
        PresentScroll(builder, resolved);
    }

    public ImmutableArray<WidgetChange> DrainChanges()
    {
        EnsureOwner();
        var result = pending.ToImmutableArray();
        pending.Clear();
        return result;
    }

    public WidgetInputResult Handle(UiInputEvent input, TimeSpan timestamp = default)
    {
        EnsureOwner();
        bool handled = input switch
        {
            UiPointerButtonChanged pointer => PointerButton(pointer, timestamp),
            UiPointerMoved pointer => PointerMove(pointer),
            UiPointerWheel wheel => Wheel(wheel),
            UiKeyChanged key => Key(key),
            UiTextEntered text => Text(text.Text, timestamp),
            UiFocusChanged { Focused: false } => SuspendInput(),
            UiPointerCaptureLost => SuspendInput(),
            UiSurfaceResized => CancelCapture(),
            _ => false
        };
        return new WidgetInputResult(handled, HasPointerCapture, DrainChanges());
    }

    public bool Focus(NodeId id, bool visible = true)
    {
        EnsureOwner();
        if (!focusOrder.Contains(id)) return false;
        if (State.Focus != id) CommitNumberDraft();
        State.Focus = id;
        State.FocusVisible = visible;
        State.SpaceArmed = null;
        SynchronizeText();
        EnsureVisible(id);
        return true;
    }

    private bool PointerButton(UiPointerButtonChanged input, TimeSpan timestamp)
    {
        var hit = RouteHit(input.Position);
        if (input.Button != UiPointerButton.Primary)
        {
            // Other buttons never activate widgets, but must not reach application controls covered by an overlay.
            if (!input.IsPressed || hit is null || !InOverlay(hit.NodeId)) return false;
            if (bindings.TryGetValue(hit.NodeId, out var covered) && covered.Kind == WidgetKind.Dismiss && covered.Id != PanelOf(covered.Popup))
                Activate(covered.Id);
            return true;
        }
        if (input.IsPressed)
        {
            State.SpaceArmed = null;
            State.Pressed = null;
            if (hit is null) { CommitNumberDraft(); State.Focus = null; return false; }
            if (bindings.TryGetValue(hit.NodeId, out var binding))
            {
                if (binding.Disabled) return true;
                Focus(hit.NodeId, false);
                if (binding.Kind is WidgetKind.Input or WidgetKind.Number)
                {
                    int clicks = ClickCount(binding.Id, input.Position, timestamp);
                    MoveTextCaret(binding, input.Position, input.Modifiers.Shift && clicks == 1);
                    if (clicks == 2) State.Text?.SelectWord();
                    else if (clicks >= 3) State.Text?.SelectAll();
                    State.SelectingText = clicks == 1;
                }
                if (binding.Kind == WidgetKind.Slider && !binding.ReadOnly)
                {
                    State.Drag = binding;
                    State.DragStart = binding.Value;
                    State.DragValue = SliderValue(binding, input.Position);
                    EmitNumber(binding, State.DragValue, WidgetEditPhase.Preview);
                }
                else State.Pressed = binding.Id;
                return true;
            }
            Focus(hit.NodeId, false);
            State.Pressed = hit.NodeId;
            return true;
        }
        if (State.Drag is { } drag)
        {
            State.DragValue = SliderValue(drag, input.Position);
            EmitNumber(drag, State.DragValue, WidgetEditPhase.Commit);
            State.Drag = null;
            return true;
        }
        var pressed = State.Pressed;
        State.Pressed = null;
        State.SelectingText = false;
        if (pressed is { } node && hit?.NodeId == node) Activate(node);
        return pressed is not null || hit?.Semantics?.Disabled == true;
    }

    /// <summary>Platform-style multi-click counting: same target, within 500 ms and 4 px.</summary>
    private int ClickCount(NodeId id, PointerPoint point, TimeSpan time)
    {
        int count = State.LastClick is { } last && last.Id == id && time >= last.Time
            && time - last.Time <= TimeSpan.FromMilliseconds(500)
            && Math.Abs(point.X - last.Point.X) <= 4 && Math.Abs(point.Y - last.Point.Y) <= 4 ? last.Count + 1 : 1;
        State.LastClick = (id, point, time, count);
        return count;
    }

    /// <summary>
    /// Hit testing with one routing rule: a combobox keeps its own input interactive while its list is open,
    /// so a click there moves the caret instead of dismissing through the backdrop.
    /// </summary>
    private UiHitTestResult? RouteHit(PointerPoint point)
    {
        var hit = hitTest?.HitTest(point);
        if (hit is null || !bindings.TryGetValue(hit.NodeId, out var binding) || binding.Kind != WidgetKind.Dismiss
            || binding.Id == PanelOf(binding.Popup)) return hit;
        var popup = popups.LastOrDefault(item => item.Id == binding.Popup);
        if (popup is not { Kind: WidgetPopupKind.Combobox } || !rectangles.TryGetValue(popup.Trigger, out var input)
            || point.X < input.X || point.Y < input.Y || point.X >= input.X + input.Width || point.Y >= input.Y + input.Height) return hit;
        return actions.TryGetValue(popup.Trigger, out var action) ? new UiHitTestResult(popup.Trigger, input, action, null) : hit;
    }

    private NodeId? PanelOf(NodeId? popup) => popup is { } id ? popups.FirstOrDefault(item => item.Id == id)?.Panel : null;

    /// <summary>True when the node belongs to any open popup's overlay (backdrop, panel or content).</summary>
    private bool InOverlay(NodeId id) => overlayNodes.Contains(id);

    private bool PointerMove(UiPointerMoved input)
    {
        State.Hover = hitTest?.HitTest(input.Position)?.NodeId;
        if (State.SelectingText && State.Pressed is { } textId && bindings.TryGetValue(textId, out var textBinding))
        {
            MoveTextCaret(textBinding, input.Position, true);
            return true;
        }
        if (State.Drag is not { } drag) return false;
        decimal value = SliderValue(drag, input.Position);
        if (value != State.DragValue)
        {
            State.DragValue = value;
            EmitNumber(drag, value, WidgetEditPhase.Preview);
        }
        return true;
    }

    private void Activate(NodeId id)
    {
        if (!bindings.TryGetValue(id, out var binding))
        {
            if (actions.TryGetValue(id, out var action)) pending.Add(new WidgetInvoked(id, action));
            return;
        }
        if (binding.Disabled || binding.ReadOnly) return;
        switch (binding.Kind)
        {
            case WidgetKind.Check:
                pending.Add(new WidgetCheckedChanged(binding.Owner, !binding.Checked, binding.Changed));
                break;
            case WidgetKind.Disclosure:
                pending.Add(new WidgetOpenChanged(binding.Owner, !binding.Open, binding.Changed));
                break;
            case WidgetKind.PopupTrigger:
                pending.Add(new WidgetOpenChanged(binding.Owner, !binding.Open, binding.Changed));
                break;
            case WidgetKind.Dismiss:
                Dismiss(binding.Popup);
                break;
            case WidgetKind.Choice:
                var selected = binding.Selected;
                string key = binding.Key!;
                if (binding.Multiple)
                    selected = selected.Contains(key) ? selected.Remove(key) : selected.Add(key);
                else if (binding.ToggleSelection && selected.Contains(key)) selected = [];
                else selected = [key];
                pending.Add(new WidgetSelectionChanged(binding.Owner, selected, binding.Changed));
                if (binding.CloseOnSelect) Dismiss(binding.Popup);
                break;
            case WidgetKind.Button:
                pending.Add(new WidgetInvoked(binding.Owner, binding.Changed) { Key = binding.Key });
                break;
        }
    }

    private bool Dismiss(NodeId? id)
    {
        var popup = id is { } node ? popups.LastOrDefault(p => p.Id == node) : popups.LastOrDefault();
        if (popup is null) return false;
        if (popup.Kind == WidgetPopupKind.Tooltip) dismissedTooltips.Add(popup.Id);
        pending.Add(new WidgetOpenChanged(popup.Id, false, popup.Changed));
        return true;
    }

    private bool CancelCapture()
    {
        bool captured = HasPointerCapture;
        if (State.Drag is { } drag) EmitNumber(drag, State.DragStart, WidgetEditPhase.Cancel);
        State.Drag = null;
        State.Pressed = null;
        State.SpaceArmed = null;
        State.SelectingText = false;
        return captured;
    }

    private bool SuspendInput()
    {
        CancelCapture();
        CommitNumberDraft();
        State.Hover = null;
        State.Search = "";
        State.LastClick = null;
        return true;
    }

    private decimal SliderValue(WidgetBinding binding, PointerPoint point)
    {
        Rect rect = rectangles[binding.Id];
        double inset = WidgetBuilder.SliderInset;
        double ratio = binding.Orientation == WidgetOrientation.Vertical
            ? 1 - (point.Y - rect.Y - inset) / Math.Max(1, rect.Height - 2 * inset)
            : (point.X - rect.X - inset) / Math.Max(1, rect.Width - 2 * inset);
        if (binding.Orientation == WidgetOrientation.Horizontal && binding.Direction == WidgetDirection.RightToLeft) ratio = 1 - ratio;
        return Snap(binding, binding.Minimum + (decimal)Math.Clamp(ratio, 0, 1) * (binding.Maximum - binding.Minimum));
    }

    internal static decimal Snap(WidgetBinding binding, decimal value)
    {
        decimal span = binding.Maximum - binding.Minimum;
        decimal delta = Math.Clamp(value, binding.Minimum, binding.Maximum) - binding.Minimum;
        decimal remainder = delta % binding.Step;
        decimal lower = delta - remainder;
        if (remainder >= binding.Step - remainder)
        {
            if (binding.Step > span - lower) return binding.Maximum;
            lower += binding.Step;
        }
        return binding.Minimum + lower;
    }

    private void EmitNumber(WidgetBinding binding, decimal value, WidgetEditPhase phase) =>
        pending.Add(new WidgetNumberChanged(binding.Owner, value, phase, binding.Changed));

    private static IEnumerable<NodeId> PreOrder(ResolvedLayoutDocument document)
    {
        var stack = new Stack<NodeId>();
        stack.Push(document.RootId);
        while (stack.TryPop(out var node))
        {
            yield return node;
            if (!document.Children.TryGetValue(node, out var children)) continue;
            for (int index = children.Count - 1; index >= 0; index--) stack.Push(children[index]);
        }
    }

    private static HashSet<NodeId> Descendants(ResolvedLayoutDocument document, NodeId root)
    {
        var result = new HashSet<NodeId>();
        var stack = new Stack<NodeId>();
        stack.Push(root);
        while (stack.TryPop(out var node))
        {
            result.Add(node);
            if (document.Children.TryGetValue(node, out var children))
                foreach (var child in children) stack.Push(child);
        }
        return result;
    }

    private void EnsureOwner()
    {
        ObjectDisposedException.ThrowIf(lifetime.IsDisposed, this);
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("WidgetSession belongs to its UI thread.");
    }

    public void Dispose() => lifetime.Dispose();
}
