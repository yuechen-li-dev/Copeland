using System.Globalization;
using Machina.Core.Measurement;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Runtime.Input;

namespace Machina.Standard.Widgets;

public sealed partial class WidgetSession
{
    private ITextMeasurer textMeasurer = DeterministicTextMeasurer.Instance;
    private WidgetStyle? textStyle;

    private void MoveTextCaret(WidgetBinding binding, PointerPoint position, bool extend)
    {
        if (State.Text is not { } selection || textStyle is null) return;
        var style = new TextStyle(textStyle.Foreground, textStyle.TextSize);
        double Measure(int index) => textMeasurer.MeasureText(selection.Text[..index], style).Width;
        var rect = rectangles[binding.Id];
        double available = Math.Max(1, rect.Width - 2 * textStyle.Padding);
        double scroll = Math.Max(0, Measure(selection.Caret) - available + 2);
        double target = position.X - rect.X - textStyle.Padding + scroll;
        var boundaries = StringInfo.ParseCombiningCharacters(selection.Text).Append(selection.Text.Length).ToArray();
        int low = 0, high = boundaries.Length - 1;
        while (low < high)
        {
            int middle = (low + high) / 2;
            double midpoint = (Measure(boundaries[middle]) + Measure(boundaries[middle + 1])) / 2;
            if (target < midpoint) high = middle;
            else low = middle + 1;
        }
        selection.Move(boundaries[low], extend);
    }
    internal (string Text, int Caret, int Anchor) TextVisual(NodeId id, string value, bool number)
    {
        if (Focused == id && State.Text is { } selection)
        {
            string display = number ? selection.Text : value;
            return (display, Math.Min(selection.Caret, display.Length), Math.Min(selection.Anchor, display.Length));
        }
        return (value, value.Length, value.Length);
    }

    private NodeId? textOwner;

    private void SynchronizeText()
    {
        if (State.Focus is not { } id || !bindings.TryGetValue(id, out var binding)
            || binding.Kind is not (WidgetKind.Input or WidgetKind.Number))
        {
            State.Text = null;
            textOwner = null;
            return;
        }
        if (textOwner != id || State.Text is null)
        {
            State.Text = new TextSelection(binding.Text);
            textOwner = id;
        }
        else if (binding.Kind == WidgetKind.Input || binding.Text != State.Text.ModelText)
        {
            if (binding.Text != State.Text.Text) State.Text.Reset(binding.Text);
            State.Text.ModelText = binding.Text;
        }
    }

    private bool EditKey(WidgetBinding binding, UiKeyChanged input)
    {
        var selection = State.Text;
        if (selection is null) return false;
        bool command = input.Modifiers.Control || input.Modifiers.Meta;
        if (command && input.Key == UiKey.A) { selection.SelectAll(); return true; }
        if (command && input.Key is UiKey.Z or UiKey.Y)
        {
            if (binding.ReadOnly) return true;
            selection.Undo(input.Key == UiKey.Y || input.Modifiers.Shift);
            if (binding.Kind == WidgetKind.Input)
                pending.Add(new WidgetTextChanged(binding.Owner, selection.Text, binding.Changed));
            return true;
        }
        if (command && input.Key is UiKey.C or UiKey.X)
        {
            if (selection.Start == selection.End) return true;
            if (Clipboard?.TryWrite(selection.Selected) != true)
            {
                pending.Add(new WidgetOperationFailed(binding.Owner, "Clipboard is unavailable."));
                return true;
            }
            if (input.Key == UiKey.C || binding.ReadOnly) return true;
            selection.Replace("", binding.MaximumLength);
        }
        else if (command && input.Key == UiKey.V)
        {
            if (binding.ReadOnly) return true;
            if (Clipboard?.TryRead(out string paste) == true) return Text(paste, default);
            pending.Add(new WidgetOperationFailed(binding.Owner, "Clipboard is unavailable."));
            return true;
        }
        else if (input.Key is UiKey.ArrowLeft or UiKey.ArrowRight)
        {
            selection.Step(input.Key == UiKey.ArrowLeft ? -1 : 1, input.Modifiers.Shift, command);
            return true;
        }
        else if (input.Key is UiKey.Home or UiKey.End)
        {
            selection.Move(input.Key == UiKey.Home ? 0 : selection.Text.Length, input.Modifiers.Shift);
            return true;
        }
        else if (input.Key is UiKey.Backspace or UiKey.Delete)
        {
            if (binding.ReadOnly) return true;
            selection.Delete(input.Key == UiKey.Backspace ? -1 : 1, command);
        }
        else if (input.Key == UiKey.Enter && binding.Kind == WidgetKind.Number)
        {
            CommitNumberDraft();
            return true;
        }
        else return false;
        if (binding.Kind == WidgetKind.Input)
            pending.Add(new WidgetTextChanged(binding.Owner, selection.Text, binding.Changed));
        return true;
    }

    private void CommitNumberDraft()
    {
        if (State.Focus is not { } id || !bindings.TryGetValue(id, out var binding)
            || binding.Kind != WidgetKind.Number || binding.ReadOnly || State.Text is not { } selection) return;
        if (!decimal.TryParse(selection.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number))
        {
            pending.Add(new WidgetOperationFailed(binding.Owner, "Enter a finite decimal number."));
            selection.Reset(binding.Text);
            return;
        }
        decimal value = Snap(binding, number);
        if (value != binding.Value) EmitNumber(binding, value, WidgetEditPhase.Commit);
    }
}
