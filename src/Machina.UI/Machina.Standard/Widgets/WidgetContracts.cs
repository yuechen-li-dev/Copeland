using System.Collections.Immutable;
using Machina.Core.Actions;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Runtime.Input;

namespace Machina.Standard.Widgets;

public enum WidgetOrientation { Horizontal, Vertical }
public enum WidgetDirection { LeftToRight, RightToLeft }
public enum WidgetActivation { Automatic, Manual }
public enum WidgetEditPhase { Preview, Commit, Cancel }
public enum WidgetPopupKind { Select, Menu, Popover, Dialog, AlertDialog, Tooltip, Drawer, Combobox }
public enum WidgetPopupSide { Bottom, Top, Left, Right }

/// <summary>
/// Which keyboard policy the widget layer currently needs from the host's shortcut system. Hosts map this to
/// their own mechanism (for example by pushing an InputMan action map): <see cref="TextEntry"/> owns unmodified
/// keys, <see cref="Popup"/> owns navigation/Escape, <see cref="Modal"/> owns every key. <see cref="Control"/>
/// means a non-text widget has focus (Enter/Space/arrows may be consumed); <see cref="None"/> leaves shortcuts alone.
/// </summary>
public enum WidgetKeyboardScope { None, Control, Popup, TextEntry, Modal }

/// <param name="Detail">Secondary text drawn right-aligned in list rows (a shortcut, a group); also matched by ranked filters.</param>
/// <param name="Keywords">Extra search terms that are matched but never drawn (synonyms, old names).</param>
/// <param name="Category">Group name drawn before the label as an aligned caps column ("EXPORT: Vertical"); matched by filters.</param>
public sealed record WidgetOption(string Key, string Label, bool Disabled = false, string? Detail = null, string? Keywords = null,
    string? Category = null)
{
    /// <summary>The label as read aloud and as plain text: "EXPORT: Vertical" when a category is set.</summary>
    public string Text => Category is { Length: > 0 } category ? category.ToUpperInvariant() + ": " + Label : Label;
}

/// <summary>Chooses and orders the options a combobox shows for a query. Must be deterministic and must not add options.</summary>
public delegate IReadOnlyList<WidgetOption> WidgetOptionFilter(IReadOnlyList<WidgetOption> options, string query);

/// <summary>Built-in combobox filters.</summary>
public static class WidgetOptionFilters
{
    /// <summary>Ordinal case-insensitive substring of the label, in declaration order (the original combobox rule).</summary>
    public static IReadOnlyList<WidgetOption> Substring(IReadOnlyList<WidgetOption> options, string query) =>
        [.. options.Where(option => option.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (option.Category ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))];

    /// <summary>
    /// Command-palette ranking. Every query word must match the label, detail or keywords. Per word, a label prefix scores
    /// highest, then a label word start ("ripple"), then a category prefix ("exp" for every EXPORT entry), then label
    /// initials ("rd" for Ripple Delete), then a label substring, then a category, detail or keyword match, then an
    /// in-order subsequence of the label ("rpdl"). Ties keep declaration order.
    /// An empty query returns every option unchanged.
    /// </summary>
    public static IReadOnlyList<WidgetOption> Ranked(IReadOnlyList<WidgetOption> options, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return options;
        var scored = new List<(WidgetOption Option, int Score, int Index)>();
        for (int i = 0; i < options.Count; i++)
        {
            int total = 0;
            foreach (string word in words)
            {
                int score = Score(options[i], word);
                if (score == 0) { total = 0; break; }
                total += score;
            }
            if (total > 0) scored.Add((options[i], total, i));
        }
        return [.. scored.OrderByDescending(item => item.Score).ThenBy(item => item.Index).Select(item => item.Option)];
    }

    private static int Score(WidgetOption option, string word)
    {
        string label = option.Label;
        if (label.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return 100;
        var starts = WordStarts(label);
        if (starts.Any(start => string.Compare(label, start, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0)) return 80;
        if ((option.Category ?? "").StartsWith(word, StringComparison.OrdinalIgnoreCase)) return 75;
        if (word.Length > 1 && Initials(label, starts).StartsWith(word, StringComparison.OrdinalIgnoreCase)) return 70;
        if (label.Contains(word, StringComparison.OrdinalIgnoreCase)) return 50;
        if ((option.Category ?? "").Contains(word, StringComparison.OrdinalIgnoreCase)
            || (option.Detail ?? "").Contains(word, StringComparison.OrdinalIgnoreCase)
            || (option.Keywords ?? "").Contains(word, StringComparison.OrdinalIgnoreCase)) return 40;
        return Subsequence(label, word) ? 10 : 0;
    }

    private static List<int> WordStarts(string text)
    {
        var starts = new List<int>();
        for (int i = 0; i < text.Length; i++)
            if (char.IsLetterOrDigit(text[i]) && (i == 0 || !char.IsLetterOrDigit(text[i - 1]))) starts.Add(i);
        return starts;
    }

    private static string Initials(string text, List<int> starts) => string.Concat(starts.Select(start => text[start]));

    private static bool Subsequence(string text, string word)
    {
        int at = 0;
        foreach (char c in text)
            if (at < word.Length && char.ToUpperInvariant(c) == char.ToUpperInvariant(word[at])) at++;
        return at == word.Length;
    }
}
public sealed record WidgetPanel(string Key, string Label, UiNode Content, bool Disabled = false);

public abstract record WidgetChange(NodeId Target, UiAction? Action);
public sealed record WidgetInvoked(NodeId Target, UiAction? Action) : WidgetChange(Target, Action)
{
    /// <summary>The stable item key when the invoked control belongs to a toolbar; null for a standalone button.</summary>
    public string? Key { get; init; }
}
public sealed record WidgetCheckedChanged(NodeId Target, bool Checked, UiAction? Action) : WidgetChange(Target, Action);
public sealed record WidgetSelectionChanged(NodeId Target, ImmutableArray<string> Keys, UiAction? Action) : WidgetChange(Target, Action);
public sealed record WidgetTextChanged(NodeId Target, string Text, UiAction? Action) : WidgetChange(Target, Action);
public sealed record WidgetNumberChanged(NodeId Target, decimal Value, WidgetEditPhase Phase, UiAction? Action) : WidgetChange(Target, Action);
public sealed record WidgetOpenChanged(NodeId Target, bool Open, UiAction? Action) : WidgetChange(Target, Action);
public sealed record WidgetOperationFailed(NodeId Target, string Message) : WidgetChange(Target, null);

/// <summary>Native hosts supply clipboard access; the component library never owns an OS clipboard.</summary>
public interface IWidgetClipboard
{
    bool TryRead(out string text);
    bool TryWrite(string text);
}

public sealed record WidgetInputResult(bool Handled, bool CapturePointer, ImmutableArray<WidgetChange> Changes);

/// <summary>Styling tokens, independent of behavior and application state.</summary>
public sealed record WidgetStyle(
    ColorToken Background,
    ColorToken Foreground,
    ColorToken Border,
    ColorToken Accent,
    ColorToken AccentForeground,
    ColorToken Muted,
    ColorToken Focus,
    double Height = 36,
    double Gap = 8,
    double Padding = 8,
    TextSize TextSize = TextSize.Sm);

internal enum WidgetKind { Button, Check, Choice, Disclosure, Slider, Input, Number, PopupTrigger, Dismiss, Scroll }

internal sealed record WidgetScrollRegion(NodeId Id, double ContentHeight, double ViewportHeight);

internal sealed record WidgetBinding(NodeId Id, NodeId Owner, WidgetKind Kind)
{
    public UiAction? Changed { get; init; }
    public UiAction? OpenChanged { get; init; }
    public bool Disabled { get; init; }
    public bool ReadOnly { get; init; }
    public bool Checked { get; init; }
    public string Text { get; init; } = "";
    public string? Key { get; init; }
    public ImmutableArray<string> Selected { get; init; } = [];
    public NodeId? Group { get; init; }
    public WidgetOrientation Orientation { get; init; }
    public WidgetDirection Direction { get; init; }
    public bool Loop { get; init; } = true;
    public bool ActivateOnFocus { get; init; }
    public bool RovingTabStop { get; init; } = true;
    public bool Multiple { get; init; }
    public bool ToggleSelection { get; init; }
    public bool CloseOnSelect { get; init; }
    public decimal Value { get; init; }
    public decimal Minimum { get; init; }
    public decimal Maximum { get; init; } = 100;
    public decimal Step { get; init; } = 1;
    public int MaximumLength { get; init; } = 4096;
    public bool DigitsOnly { get; init; }
    public bool Open { get; init; }
    public NodeId? Popup { get; init; }
    /// <summary>Combobox input: Enter accepts the first enabled suggestion while the list is open.</summary>
    public bool AcceptFirst { get; init; }

    public UiAction Intent => UiAction.Typed(new UiActionId(Id.Value), new WidgetIntent(Id));
}

internal sealed record WidgetIntent(NodeId Node);
internal sealed record WidgetPopup(NodeId Id, NodeId Trigger, UiNode Content, WidgetPopupKind Kind,
    string Title, double Width, double Height, WidgetPopupSide Side, bool Modal,
    bool DismissOutside, bool DismissEscape, UiAction? Changed)
{
    public NodeId Panel => Id.Child("popup");
    /// <summary>Exact list content height for select/menu/combobox panels; null for free content.</summary>
    public double? ListHeight { get; init; }
    /// <summary>List rows built at overlay time for the final row width (narrower when a scroll gutter is needed).</summary>
    public Func<double, UiNode>? List { get; init; }
    public Rect? PointAnchor { get; init; }
    public double EntranceOffset { get; init; }
}

/// <summary>Viewport-local placement, flipping when possible and clamping without negative dimensions.</summary>
public static class WidgetPopupPlacement
{
    public static Rect Place(Rect anchor, Rect viewport, double width, double height,
        WidgetPopupSide side = WidgetPopupSide.Bottom, double gap = 4)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0
            || !double.IsFinite(gap) || gap < 0 || viewport.Width <= 0 || viewport.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Popup and viewport dimensions must be positive and finite.");
        width = Math.Min(width, viewport.Width);
        height = Math.Min(height, viewport.Height);
        double x = anchor.X, y = anchor.Y + anchor.Height + gap;
        switch (side)
        {
            case WidgetPopupSide.Top:
                y = anchor.Y - height - gap;
                if (y < viewport.Y) y = anchor.Y + anchor.Height + gap;
                break;
            case WidgetPopupSide.Bottom:
                if (y + height > viewport.Y + viewport.Height) y = anchor.Y - height - gap;
                break;
            case WidgetPopupSide.Left:
                x = anchor.X - width - gap;
                y = anchor.Y;
                if (x < viewport.X) x = anchor.X + anchor.Width + gap;
                break;
            case WidgetPopupSide.Right:
                x = anchor.X + anchor.Width + gap;
                y = anchor.Y;
                if (x + width > viewport.X + viewport.Width) x = anchor.X - width - gap;
                break;
        }
        return new Rect(Math.Clamp(x, viewport.X, viewport.X + viewport.Width - width),
            Math.Clamp(y, viewport.Y, viewport.Y + viewport.Height - height), width, height);
    }
}
