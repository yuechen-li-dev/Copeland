using System.Collections.Immutable;
using Machina.Core.Actions;

namespace Machina.Standard.Widgets;

/// <summary>
/// A controlled value and where its proposals go, declared once. The getter supplies the value the widget shows;
/// the setters receive the widget's proposals when the host calls <see cref="WidgetSession.Apply"/>. Because value
/// and destination come from one declaration they cannot drift apart, and typed overloads reject the wrong kind
/// (a checkbox only accepts <c>Bound&lt;bool&gt;</c>, a slider <c>Bound&lt;decimal&gt;</c>).
/// </summary>
public sealed class Bound<T>
{
    private readonly Func<T> get;
    private readonly Action<T> commit;
    private readonly Action<T> preview;
    private readonly Action<T> cancel;

    internal Bound(Func<T> get, Action<T> commit, Action<T>? preview, Action<T>? cancel)
    {
        this.get = get ?? throw new ArgumentNullException(nameof(get));
        this.commit = commit ?? throw new ArgumentNullException(nameof(commit));
        this.preview = preview ?? commit;
        this.cancel = cancel ?? this.preview;
    }

    /// <summary>The current controlled value, read when the widget is declared.</summary>
    public T Value => get();

    internal void Apply(T value, WidgetEditPhase phase)
    {
        switch (phase)
        {
            case WidgetEditPhase.Preview: preview(value); break;
            case WidgetEditPhase.Cancel: cancel(value); break;
            default: commit(value); break;
        }
    }
}

/// <summary>Constructors for <see cref="Bound{T}"/>.</summary>
public static class Bind
{
    /// <summary>A plain controlled value: every proposal (including slider previews) is written with <paramref name="set"/>.</summary>
    public static Bound<T> Value<T>(Func<T> get, Action<T> set) => new(get, set, null, null);

    /// <summary>
    /// A gesture-shaped edit. <paramref name="preview"/> receives transient values during a drag and must not create an
    /// application undo entry; <paramref name="commit"/> runs once at the end; <paramref name="cancel"/> receives the
    /// gesture's original value (defaults to <paramref name="preview"/>, which restores the transient state).
    /// </summary>
    public static Bound<T> Edit<T>(Func<T> get, Action<T> preview, Action<T> commit, Action<T>? cancel = null) =>
        new(get, commit, preview ?? throw new ArgumentNullException(nameof(preview)), cancel);
}

/// <summary>The payload of a bound widget's action: applies the proposals that widget can produce.</summary>
internal sealed class WidgetSink(Func<WidgetChange, bool> apply)
{
    public static readonly UiActionId Id = new("machina.widgets.bound");
    public bool TryApply(WidgetChange change) => apply(change);
    public UiAction Action => UiAction.Typed(Id, this);

    public static UiAction Checked(Bound<bool> bound) => new WidgetSink(change =>
        change is WidgetCheckedChanged c && Run(() => bound.Apply(c.Checked, WidgetEditPhase.Commit))).Action;
    public static UiAction Open(Bound<bool> bound) => new WidgetSink(change =>
        change is WidgetOpenChanged o && Run(() => bound.Apply(o.Open, WidgetEditPhase.Commit))).Action;
    public static UiAction Text(Bound<string> bound) => new WidgetSink(change =>
        change is WidgetTextChanged t && Run(() => bound.Apply(t.Text, WidgetEditPhase.Commit))).Action;
    public static UiAction Number(Bound<decimal> bound) => new WidgetSink(change =>
        change is WidgetNumberChanged n && Run(() => bound.Apply(n.Value, n.Phase))).Action;
    public static UiAction Single(Bound<string?> bound) => new WidgetSink(change =>
        change is WidgetSelectionChanged s && Run(() => bound.Apply(s.Keys.IsDefaultOrEmpty ? null : s.Keys[0], WidgetEditPhase.Commit))).Action;
    public static UiAction Multiple(Bound<ImmutableArray<string>> bound) => new WidgetSink(change =>
        change is WidgetSelectionChanged s && Run(() => bound.Apply(s.Keys.IsDefault ? [] : s.Keys, WidgetEditPhase.Commit))).Action;
    public static UiAction Invoked(Action invoked) => new WidgetSink(change =>
        change is WidgetInvoked && Run(invoked)).Action;
    /// <summary>Toolbar items (<see cref="WidgetInvoked.Key"/>) and menu items (a one-key selection).</summary>
    public static UiAction Item(Action<string> invoked) => new WidgetSink(change => change switch
    {
        WidgetInvoked { Key: { } key } => Run(() => invoked(key)),
        WidgetSelectionChanged { Keys.Length: > 0 } s => Run(() => invoked(s.Keys[0])),
        _ => false,
    }).Action;
    /// <summary>A combobox's query (text) and selected key share one action; route each to its own binding.</summary>
    public static UiAction Combobox(Bound<string?> selected, Bound<string> query) => new WidgetSink(change => change switch
    {
        WidgetTextChanged t => Run(() => query.Apply(t.Text, WidgetEditPhase.Commit)),
        WidgetSelectionChanged s => Run(() => selected.Apply(s.Keys.IsDefaultOrEmpty ? null : s.Keys[0], WidgetEditPhase.Commit)),
        _ => false,
    }).Action;

    private static bool Run(Action action) { action(); return true; }
}
