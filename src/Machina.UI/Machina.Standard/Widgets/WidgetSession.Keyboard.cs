using Machina.Layout.Rows;
using Machina.Runtime.Input;

namespace Machina.Standard.Widgets;

public sealed partial class WidgetSession
{
    private bool Key(UiKeyChanged input)
    {
        if (!input.IsPressed)
        {
            if (input.Key != UiKey.Space || State.SpaceArmed is not { } armed) return false;
            State.SpaceArmed = null;
            if (State.Focus == armed) Activate(armed);
            return true;
        }
        if (input.Key == UiKey.Escape)
        {
            if (HasPointerCapture) return CancelCapture();
            if (popups.LastOrDefault() is { DismissEscape: true } top) return Dismiss(top.Id);
            return false;
        }
        if (input.Key == UiKey.Tab)
        {
            CommitNumberDraft();
            var top = popups.LastOrDefault();
            if (top is { Kind: WidgetPopupKind.Menu or WidgetPopupKind.Select or WidgetPopupKind.Combobox })
            {
                Dismiss(top.Id);
                State.Focus = top.Trigger;
            }
            var stops = TabStops();
            if (top is { Kind: WidgetPopupKind.Menu or WidgetPopupKind.Select or WidgetPopupKind.Combobox })
                stops.RemoveAll(id => popupChildren[top.Id].Contains(id));
            if (stops.Count == 0) return popups.Any(p => p.Modal);
            int index = State.Focus is { } focus ? stops.IndexOf(focus) : -1;
            index = input.Modifiers.Shift
                ? (index <= 0 ? stops.Count - 1 : index - 1)
                : (index + 1) % stops.Count;
            Focus(stops[index]);
            return true;
        }
        if (State.Focus is not { } id) return false;
        if (bindings.TryGetValue(id, out var binding))
        {
            if (binding.Disabled) return false;
            if (binding.Kind == WidgetKind.Scroll && ScrollKey(binding, input.Key)) return true;
            if (binding.Kind is WidgetKind.Input or WidgetKind.Number && EditKey(binding, input)) return true;
            if (binding.Kind == WidgetKind.Input && binding.Popup is { } popupId
                && input.Key is UiKey.ArrowDown or UiKey.ArrowUp)
            {
                if (!binding.Open) pending.Add(new WidgetOpenChanged(popupId, true, binding.OpenChanged));
                else
                {
                    var options = focusOrder.Where(bindings.ContainsKey).Select(id => bindings[id])
                        .Where(item => item.Kind == WidgetKind.Choice && item.Popup == popupId).ToList();
                    var option = input.Key == UiKey.ArrowDown ? options.FirstOrDefault() : options.LastOrDefault();
                    if (option is not null) Focus(option.Id);
                }
                return true;
            }
            if (binding.Kind is WidgetKind.Slider or WidgetKind.Number && NumericKey(binding, input)) return true;
            if (binding.Group is not null && NavigateGroup(binding, input)) return true;
            if (binding.Kind == WidgetKind.PopupTrigger && input.Key is UiKey.ArrowDown or UiKey.ArrowUp)
            {
                if (!binding.Open) Activate(id);
                return true;
            }
        }
        if (input.Key == UiKey.Enter && !input.IsRepeat)
        {
            Activate(id);
            return true;
        }
        if (input.Key == UiKey.Space && !input.IsRepeat)
        {
            State.SpaceArmed = id;
            return true;
        }
        return false;
    }

    private List<NodeId> TabStops()
    {
        var result = new List<NodeId>();
        var visited = new HashSet<NodeId>();
        foreach (var id in focusOrder)
        {
            if (!bindings.TryGetValue(id, out var binding) || !binding.RovingTabStop || binding.Group is not { } group)
            {
                result.Add(id);
                continue;
            }
            if (!visited.Add(group)) continue;
            var members = GroupMembers(binding);
            var active = members.FirstOrDefault(member => member.Id == State.Focus)
                ?? members.FirstOrDefault(member => member.Selected.Contains(member.Key ?? ""))
                ?? members.FirstOrDefault();
            if (active is not null) result.Add(active.Id);
        }
        return result;
    }

    private List<WidgetBinding> GroupMembers(WidgetBinding binding) => focusOrder
        .Where(bindings.ContainsKey).Select(id => bindings[id])
        .Where(member => member.Group == binding.Group && !member.Disabled).ToList();

    private bool NavigateGroup(WidgetBinding binding, UiKeyChanged input)
    {
        bool horizontal = binding.Orientation == WidgetOrientation.Horizontal;
        int direction = 0;
        if (horizontal && input.Key == UiKey.ArrowRight || !horizontal && input.Key == UiKey.ArrowDown) direction = 1;
        if (horizontal && input.Key == UiKey.ArrowLeft || !horizontal && input.Key == UiKey.ArrowUp) direction = -1;
        if (horizontal && binding.Direction == WidgetDirection.RightToLeft) direction = -direction;
        if (direction == 0 && input.Key is not (UiKey.Home or UiKey.End)) return false;
        var members = GroupMembers(binding);
        if (members.Count == 0) return true;
        int index = members.FindIndex(member => member.Id == binding.Id);
        if (input.Key == UiKey.Home) index = 0;
        else if (input.Key == UiKey.End) index = members.Count - 1;
        else if (binding.Loop) index = (index + direction + members.Count) % members.Count;
        else index = Math.Clamp(index + direction, 0, members.Count - 1);
        var target = members[index];
        Focus(target.Id);
        if (target.ActivateOnFocus) Activate(target.Id);
        return true;
    }

    private bool NumericKey(WidgetBinding binding, UiKeyChanged input)
    {
        if (binding.ReadOnly) return false;
        decimal value = binding.Value;
        decimal direction = binding.Direction == WidgetDirection.RightToLeft ? -1 : 1;
        decimal span = binding.Maximum - binding.Minimum;
        decimal pageStep = binding.Step > span / 10 ? span : binding.Step * 10;
        switch (input.Key)
        {
            case UiKey.Home when binding.Kind == WidgetKind.Slider: value = binding.Minimum; break;
            case UiKey.End when binding.Kind == WidgetKind.Slider: value = binding.Maximum; break;
            case UiKey.PageUp: value += Math.Min(binding.Maximum - value, pageStep); break;
            case UiKey.PageDown: value -= Math.Min(value - binding.Minimum, pageStep); break;
            case UiKey.ArrowUp: value = Math.Min(binding.Maximum, value + Math.Min(binding.Maximum - value, binding.Step)); break;
            case UiKey.ArrowDown: value = Math.Max(binding.Minimum, value - Math.Min(value - binding.Minimum, binding.Step)); break;
            case UiKey.ArrowRight when binding.Kind == WidgetKind.Slider:
                value += direction * Math.Min(direction > 0 ? binding.Maximum - value : value - binding.Minimum, binding.Step);
                break;
            case UiKey.ArrowLeft when binding.Kind == WidgetKind.Slider:
                value -= direction * Math.Min(direction > 0 ? value - binding.Minimum : binding.Maximum - value, binding.Step);
                break;
            default: return false;
        }
        EmitNumber(binding, Snap(binding, value), WidgetEditPhase.Commit);
        return true;
    }

    private bool Text(string text, TimeSpan timestamp)
    {
        if (State.Focus is not { } id || !bindings.TryGetValue(id, out var binding)) return false;
        if (binding.Kind is WidgetKind.Input or WidgetKind.Number)
        {
            if (binding.ReadOnly) return true;
            var selection = State.Text;
            if (selection is null) return false;
            string inserted = string.Concat(text.EnumerateRunes().Where(rune => !System.Text.Rune.IsControl(rune)
                && (!binding.DigitsOnly || rune.Value is >= '0' and <= '9')).Take(binding.MaximumLength).Select(rune => rune.ToString()));
            selection.Replace(inserted, binding.MaximumLength);
            if (binding.Kind == WidgetKind.Input)
                pending.Add(new WidgetTextChanged(binding.Owner, selection.Text, binding.Changed));
            if (binding.Kind == WidgetKind.Input && binding.Popup is { } popup && !binding.Open)
                pending.Add(new WidgetOpenChanged(popup, true, binding.OpenChanged));
            return true;
        }
        if (binding.Group is null || text.Any(char.IsControl)) return false;
        if (timestamp - State.SearchTime > TimeSpan.FromSeconds(1) || timestamp < State.SearchTime) State.Search = "";
        State.SearchTime = timestamp;
        State.Search = (State.Search + text).Length <= 64 ? State.Search + text : text;
        var members = GroupMembers(binding);
        int start = members.FindIndex(member => member.Id == id);
        string search = State.Search.All(c => char.ToUpperInvariant(c) == char.ToUpperInvariant(State.Search[0]))
            ? State.Search[..1] : State.Search;
        for (int offset = 1; offset <= members.Count; offset++)
        {
            var candidate = members[(start + offset) % members.Count];
            if (!candidate.Text.StartsWith(search, StringComparison.OrdinalIgnoreCase)) continue;
            Focus(candidate.Id);
            if (candidate.ActivateOnFocus) Activate(candidate.Id);
            break;
        }
        return true;
    }
}
