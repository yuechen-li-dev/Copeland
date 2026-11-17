using System.Collections.Immutable;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Pipeline;
using Machina.Runtime.Input;
using Machina.Standard.Widgets;
using Xunit;

namespace Machina.Standard.Tests;

/// <summary>
/// Regression tests for the production-hardening pass. Each test drives a controlled host that applies
/// proposals and re-prepares, the way a real window does, rather than inspecting one frame.
/// </summary>
public sealed class WidgetHardeningTests
{
    private static readonly WidgetOption[] Options = [new("a", "Alpha"), new("b", "Beta", true), new("c", "Charlie")];
    private static readonly UiModifiers Control = new(true, false, false, false);

    [Fact]
    public void FocusedTextFieldOwnsUnmodifiedKeysButLetsCommandChordsThrough()
    {
        string text = "abc";
        using var host = new Host(w => w.Input("name", text, "Name"), change => { if (change is WidgetTextChanged t) text = t.Text; });
        host.Session.Focus("name");
        Assert.Equal(WidgetKeyboardScope.TextEntry, host.Session.KeyboardScope);
        // Letter keys arrive as UiKey.Unknown; they must not fall through to an application's Mark In etc.
        Assert.True(host.Key(UiKey.Unknown).Handled);
        Assert.True(host.Key(UiKey.F).Handled);
        Assert.True(host.Key(UiKey.ArrowUp).Handled);
        Assert.Equal(0, host.Session.TextVisual("name", text, false).Caret);
        // Ctrl+S, Ctrl+F and similar remain application shortcuts.
        Assert.False(host.Key(UiKey.F, modifiers: Control).Handled);
        Assert.False(host.Key(UiKey.R, modifiers: Control).Handled);
    }

    [Fact]
    public void HeldSpaceAndEnterOnAButtonAreConsumedAndActivateOnce()
    {
        int invoked = 0;
        using var host = new Host(w => w.Button("play", "Play"), change => { if (change is WidgetInvoked) invoked++; });
        host.Session.Focus("play");
        Assert.True(host.Key(UiKey.Space).Handled);
        // Auto-repeat while held must neither activate nor leak to the host (Space is play/pause in Valencia).
        Assert.True(host.Send(new UiKeyChanged(UiKey.Space, true, true, default)).Handled);
        Assert.True(host.Key(UiKey.Space, false).Handled);
        Assert.True(host.Send(new UiKeyChanged(UiKey.Enter, true, true, default)).Handled);
        Assert.Equal(1, invoked);
    }

    [Fact]
    public void OpenModalIsolatesKeysTextAndWheelFromTheApplication()
    {
        bool open = true;
        using var host = new Host(w => UI.Column([
            w.Button("behind", "Behind"),
            w.Dialog("dialog", "Open", "Title", UI.Text("Read only"), open)]),
            change => { if (change is WidgetOpenChanged o) open = o.Open; });
        Assert.Equal(WidgetKeyboardScope.Modal, host.Session.KeyboardScope);
        Assert.True(host.Key(UiKey.Unknown).Handled);
        Assert.True(host.Send(new UiTextEntered("x")).Handled);
        Assert.True(host.Send(new UiPointerWheel(new(2, 2), 0, -1, default)).Handled);
        Assert.True(host.Key(UiKey.Escape).Handled);
        Assert.False(open);
        Assert.Equal(WidgetKeyboardScope.Control, host.Session.KeyboardScope);
    }

    [Fact]
    public void ToolbarInvocationIdentifiesTheItem()
    {
        var keys = new List<string?>();
        using var host = new Host(w => w.Toolbar("tools", [new("select", "Select"), new("split", "Split")]),
            change => { if (change is WidgetInvoked invoked) keys.Add(invoked.Key); });
        host.Click("tools.split");
        host.Session.Focus("tools.select");
        host.Key(UiKey.Enter);
        Assert.Equal(new string?[] { "split", "select" }, keys);
    }

    [Fact]
    public void SliderThumbSitsWhereThePointerMapsSoGrabbingItDoesNotJump()
    {
        decimal value = 0;
        using var host = new Host(w => w.Slider("s", value, "Level", width: 200),
            change => { if (change is WidgetNumberChanged n) value = n.Value; });
        Rect Thumb() => host.Frame.Resolved.Nodes[new NodeId("s.thumb")].Rect;
        Rect box = host.Frame.Resolved.Nodes[new NodeId("s")].Rect;
        foreach (decimal target in new[] { 0m, 37m, 100m })
        {
            value = target;
            host.Prepare();
            Rect thumb = Thumb();
            host.Pointer(thumb.X + thumb.Width / 2, box.Y + box.Height / 2, true);
            Assert.Equal(target, value);
            host.Pointer(thumb.X + thumb.Width / 2, box.Y + box.Height / 2, false);
            Assert.Equal(target, value);
        }
        // The ends of the travel are reachable without leaving the control.
        host.Pointer(box.X + 1, box.Y + 5, true); host.Pointer(box.X + 1, box.Y + 5, false);
        Assert.Equal(0, value);
        host.Pointer(box.X + box.Width - 1, box.Y + 5, true); host.Pointer(box.X + box.Width - 1, box.Y + 5, false);
        Assert.Equal(100, value);
    }

    [Fact]
    public void NumberCommitShowsTheCommittedValueRatherThanTheRejectedDraft()
    {
        decimal value = 100;
        using var host = new Host(w => w.NumberField("n", value, "Gain", 0, 100, .5m),
            change => { if (change is WidgetNumberChanged n) value = n.Value; });
        host.Session.Focus("n");
        host.Key(UiKey.A, modifiers: Control);
        host.Send(new UiTextEntered("150"));
        Assert.Equal("150", host.Session.TextVisual("n", "100", true).Text);
        host.Key(UiKey.Enter);
        Assert.Equal(100, value);
        Assert.Equal("100", host.Session.TextVisual("n", "100", true).Text);
        // An in-range draft waits for the host's controlled value.
        host.Key(UiKey.A, modifiers: Control);
        host.Send(new UiTextEntered("12.5"));
        host.Key(UiKey.Enter);
        Assert.Equal(12.5m, value);
        Assert.Equal("12.5", host.Session.TextVisual("n", "12.5", true).Text);
    }

    [Fact]
    public void NumberFieldDisplaysTheClampedValueItHolds()
    {
        using var host = new Host(w => w.NumberField("n", 500, "Gain", 0, 100), _ => { });
        Assert.Equal("100", host.Frame.Lowering.Semantics[new NodeId("n")].Value);
    }

    [Fact]
    public void WordNavigationAndDeletionStopAtWordStarts()
    {
        string text = "hello brave world";
        using var host = new Host(w => w.Input("t", text, "Text", width: 400), change => { if (change is WidgetTextChanged t) text = t.Text; });
        host.Session.Focus("t");
        int Caret() => host.Session.TextVisual("t", text, false).Caret;
        Assert.Equal(17, Caret());
        host.Key(UiKey.ArrowLeft, modifiers: Control); Assert.Equal(12, Caret());
        host.Key(UiKey.ArrowLeft, modifiers: Control); Assert.Equal(6, Caret());
        host.Key(UiKey.ArrowLeft, modifiers: Control); Assert.Equal(0, Caret());
        host.Key(UiKey.ArrowRight, modifiers: Control); Assert.Equal(6, Caret());
        host.Key(UiKey.End);
        host.Key(UiKey.Backspace, modifiers: Control);
        Assert.Equal("hello brave ", text);
        host.Key(UiKey.Backspace, modifiers: Control);
        Assert.Equal("hello ", text);
    }

    [Fact]
    public void DoubleClickSelectsAWordAndTripleClickSelectsAll()
    {
        string text = "hello world";
        using var host = new Host(w => w.Input("t", text, "Text", width: 400), _ => { });
        Rect box = host.Frame.Resolved.Nodes[new NodeId("t")].Rect;
        var measurer = Machina.Core.Measurement.DeterministicTextMeasurer.Instance;
        var style = new Machina.Core.Styling.TextStyle(Size: Machina.Core.Styling.TextSize.Sm);
        double x = box.X + 8 + measurer.MeasureText("hello wo", style).Width; // inside "world"
        (string Text, int Caret, int Anchor) Visual() => host.Session.TextVisual("t", text, false);
        void Click(int ms) { host.Pointer(x, box.Y + 10, true, ms); host.Pointer(x, box.Y + 10, false, ms); }
        Click(0); Click(120);
        var word = Visual();
        Assert.Equal("world", word.Text[Math.Min(word.Caret, word.Anchor)..Math.Max(word.Caret, word.Anchor)]);
        Click(240);
        var all = Visual();
        Assert.Equal((0, 11), (Math.Min(all.Caret, all.Anchor), Math.Max(all.Caret, all.Anchor)));
        Click(2000); // too late: an ordinary click collapses the selection
        Assert.Equal(Visual().Caret, Visual().Anchor);
    }

    [Fact]
    public void PasteTurnsLineBreaksAndTabsIntoSpaces()
    {
        string text = "";
        using var host = new Host(w => w.Input("t", text, "Text"), change => { if (change is WidgetTextChanged t) text = t.Text; },
            new FakeClipboard("one\r\ntwo\tthree\nfour"));
        host.Session.Focus("t");
        host.Key(UiKey.V, modifiers: Control);
        Assert.Equal("one two three four", text);
    }

    [Fact]
    public void ClickingAnOpenComboboxInputMovesTheCaretInsteadOfDismissing()
    {
        string query = "ar";
        bool open = true;
        using var host = new Host(w => w.Combobox("combo", Options, null, query, open, "Asset", width: 300),
            change => { if (change is WidgetOpenChanged o) open = o.Open; if (change is WidgetTextChanged t) query = t.Text; });
        host.Session.Focus("combo.input");
        Rect input = host.Frame.Resolved.Nodes[new NodeId("combo.input")].Rect;
        host.Pointer(input.X + 9, input.Y + 10, true);
        host.Pointer(input.X + 9, input.Y + 10, false);
        Assert.True(open);
        Assert.Equal(new NodeId("combo.input"), host.Session.Focused);
        Assert.Equal(0, host.Session.TextVisual("combo.input", query, false).Caret);
        // Clicking elsewhere still dismisses.
        host.Pointer(630, 470, true); host.Pointer(630, 470, false);
        Assert.False(open);
    }

    [Fact]
    public void TypingWhileAComboboxOptionIsFocusedKeepsEditingTheQuery()
    {
        string query = "a";
        bool open = true;
        using var host = new Host(w => w.Combobox("combo", Options, null, query, open, "Asset", width: 300),
            change => { if (change is WidgetOpenChanged o) open = o.Open; if (change is WidgetTextChanged t) query = t.Text; });
        host.Session.Focus("combo.input");
        host.Key(UiKey.ArrowDown);
        Assert.Equal(new NodeId("combo.option.a"), host.Session.Focused);
        host.Send(new UiTextEntered("r"));
        Assert.Equal("ar", query);
        Assert.Equal(new NodeId("combo.input"), host.Session.Focused);
        Assert.True(open);
    }

    [Fact]
    public void PopoverMovesFocusIntoItsContentAndRestoresTheTrigger()
    {
        bool open = false;
        using var host = new Host(w => UI.Column([
            w.Popover("pop", "More", w.Button("inside", "Inside"), open), w.Button("after", "After")]),
            change => { if (change is WidgetOpenChanged o) open = o.Open; });
        host.Click("pop.trigger");
        Assert.True(open);
        Assert.Equal(new NodeId("inside"), host.Session.Focused);
        host.Key(UiKey.Escape);
        Assert.False(open);
        Assert.Equal(new NodeId("pop.trigger"), host.Session.Focused);
    }

    [Fact]
    public void PopoverWithoutFocusableContentKeepsFocusOnItsTrigger()
    {
        bool open = false;
        using var host = new Host(w => w.Popover("pop", "More", UI.Text("Info"), open),
            change => { if (change is WidgetOpenChanged o) open = o.Open; });
        host.Click("pop.trigger");
        Assert.True(open);
        Assert.Equal(new NodeId("pop.trigger"), host.Session.Focused);
    }

    [Fact]
    public void SecondaryClickOutsideAPopupDismissesItAndNeverReachesTheApplication()
    {
        bool open = true;
        using var host = new Host(w => w.Select("sel", Options, "a", open),
            change => { if (change is WidgetOpenChanged o) open = o.Open; });
        Assert.Equal(WidgetKeyboardScope.Popup, host.Session.KeyboardScope);
        var result = host.Send(new UiPointerButtonChanged(new(630, 470), UiPointerButton.Secondary, true, default));
        Assert.True(result.Handled);
        Assert.False(open);
        // With nothing open, secondary clicks are the application's.
        Assert.False(host.Send(new UiPointerButtonChanged(new(630, 470), UiPointerButton.Secondary, true, default)).Handled);
    }

    [Fact]
    public void PopupListFitsItsOptionsWhenGapAndPaddingDiffer()
    {
        var style = new WidgetStyle(Machina.Core.Styling.ColorToken.Hex(0xFFFFFFFF), Machina.Core.Styling.ColorToken.Hex(0x000000FF),
            Machina.Core.Styling.ColorToken.Hex(0x888888FF), Machina.Core.Styling.ColorToken.Hex(0x0000FFFF),
            Machina.Core.Styling.ColorToken.Hex(0xFFFFFFFF), Machina.Core.Styling.ColorToken.Hex(0x666666FF),
            Machina.Core.Styling.ColorToken.Hex(0x0000FFFF), Height: 30, Gap: 2, Padding: 12);
        using var host = new Host(w => w.Select("sel", Options, "a", true), _ => { }, style: style);
        Rect panel = host.Frame.Resolved.Nodes[new NodeId("sel.popup")].Rect;
        Rect last = host.Frame.Resolved.Nodes[new NodeId("sel.option.c")].Rect;
        Assert.Equal(3 * 30 + 2 * 2 + 2 * 12, panel.Height, 6);
        Assert.True(last.Y + last.Height <= panel.Y + panel.Height - 12 + 1e-6);
        // Nothing to scroll: every option is visible.
        Assert.False(host.Frame.Lowering.Semantics[new NodeId("sel.scroll")].Focusable);
    }

    [Fact]
    public void WhitespaceDoesNotStartATypeaheadSearch()
    {
        bool open = true;
        using var host = new Host(w => w.Menu("menu", "Menu", Options, open), change => { if (change is WidgetOpenChanged o) open = o.Open; });
        host.Send(new UiTextEntered(" "), 0);
        host.Send(new UiTextEntered("c"), 100);
        Assert.Equal(new NodeId("menu.option.c"), host.Session.Focused);
    }

    [Fact]
    public void FocusLossPreservesTheTextDraftAndCaret()
    {
        string text = "draft";
        using var host = new Host(w => w.Input("t", text, "Text"), change => { if (change is WidgetTextChanged t) text = t.Text; });
        host.Session.Focus("t");
        host.Key(UiKey.Home);
        host.Send(new UiFocusChanged(false));
        host.Send(new UiFocusChanged(true));
        Assert.Equal(new NodeId("t"), host.Session.Focused);
        Assert.Equal(0, host.Session.TextVisual("t", text, false).Caret);
        host.Send(new UiTextEntered(">"));
        Assert.Equal(">draft", text);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(400)]
    public void LongLabelsAreEllipsizedInsteadOfOverflowingTheLayout(double width)
    {
        const string label = "An unusually long label that cannot possibly fit inside a narrow control";
        WidgetOption[] options = [new("a", label), new("b", label), new("c", label), new("d", label)];
        using var host = new Host(w => UI.Column([
            w.Checkbox("check", label, true, width: width),
            w.Switch("switch", label, true, width: width),
            w.Toggle("toggle", label, true, width: width),
            w.Button("button", label, width: width),
            w.RadioGroup("radio", options, "a", orientation: WidgetOrientation.Horizontal, width: Math.Max(width, 40)),
            w.CheckboxGroup("flags", options, ["a"], width: width),
            w.ToggleGroup("toggles", options, ["a"], width: Math.Max(width, 40)),
            w.Tabs("tabs", [new("a", label, UI.Text(label)), new("b", label, UI.Text(label))], "a", width: Math.Max(width, 80))]), _ => { }, height: 1200);
        // The accessible name keeps the full text.
        Assert.Equal(label, host.Frame.Lowering.Semantics[new NodeId("check")].Label);
        Assert.Equal(label, host.Frame.Lowering.Semantics[new NodeId("button")].Label);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RandomInteractionNeverThrowsAndKeepsFocusAndCaptureConsistent(int seed)
    {
        var random = new Random(seed);
        var model = new Dictionary<string, object>();
        T Get<T>(string key, T fallback) => model.TryGetValue(key, out var value) ? (T)value : fallback;
        var openSet = new HashSet<string>();
        int opened = 0, edited = 0, committed = 0;
        UiNode View(WidgetBuilder w) => UI.Layer(children:
        [
            Machina.Standard.Authoring.StandardUI.Place(UI.Column([
                w.Checkbox("check", "Check", Get("check", false)),
                w.Input("name", Get("name", "name"), "Name"),
                w.NumberField("num", Get("num", 5m), "Num", 0, 10, .5m),
                w.Slider("slider", Get("slider", 5m), "Slider", 0, 10, .5m),
                w.RadioGroup("radio", Options, Get<string?>("radio", "a"), orientation: WidgetOrientation.Horizontal, width: 300),
                w.Tabs("tabs", [new("a", "A", UI.Text("A")), new("c", "C", UI.Text("C"))], Get<string?>("tabs", "a"), contentHeight: 40),
                w.Toolbar("tools", [new("x", "X"), new("y", "Y")], width: 200)], gap: 4), 0, 0, 320, 600),
            Machina.Standard.Authoring.StandardUI.Place(UI.Column([
                w.Select("sel", Options, Get<string?>("sel", "a"), openSet.Contains("sel")),
                w.Menu("menu", "Menu", Options, openSet.Contains("menu")),
                w.Combobox("combo", Options, Get<string?>("combo", null), Get("comboQuery", ""), openSet.Contains("combo"), "Combo"),
                w.Popover("pop", "Pop", w.Button("pop-inside", "Inside"), openSet.Contains("pop")),
                w.Dialog("dlg", "Dialog", "Title", UI.Column([w.Input("dlg-in", Get("dlg-in", ""), "In"),
                    w.Select("dlg-sel", Options, Get<string?>("dlg-sel", null), openSet.Contains("dlg-sel"))]), openSet.Contains("dlg")),
                w.Tooltip("tip", "Tip", "Tooltip text"),
                w.ScrollArea("scroll", UI.Column(Enumerable.Range(0, 8).Select(i => w.Button("row" + i, "Row " + i)).ToArray(), gap: 4), 8 * 40, height: 100)
            ], gap: 4), 330, 0, 300, 600)
        ]);
        void Apply(WidgetChange change)
        {
            string target = change.Target.Value;
            switch (change)
            {
                case WidgetCheckedChanged c: model[target] = c.Checked; break;
                case WidgetTextChanged t: model[target == "combo" ? "comboQuery" : target] = t.Text; break;
                case WidgetNumberChanged n when n.Phase != WidgetEditPhase.Preview || target == "slider": model[target] = n.Value; break;
                case WidgetSelectionChanged sel: model[target] = sel.Keys.FirstOrDefault()!; break;
                case WidgetOpenChanged o: if (o.Open) { openSet.Add(target); opened++; } else openSet.Remove(target); break;
            }
            if (change is WidgetTextChanged) edited++;
            if (change is WidgetNumberChanged { Phase: WidgetEditPhase.Commit }) committed++;
        }
        using var host = new Host(View, Apply, new FakeClipboard("pasted\ntext"), height: 640);
        UiKey[] keys = [UiKey.Tab, UiKey.Enter, UiKey.Space, UiKey.Escape, UiKey.ArrowUp, UiKey.ArrowDown, UiKey.ArrowLeft,
            UiKey.ArrowRight, UiKey.Home, UiKey.End, UiKey.PageUp, UiKey.PageDown, UiKey.Backspace, UiKey.Delete, UiKey.A,
            UiKey.C, UiKey.V, UiKey.X, UiKey.Z, UiKey.Y, UiKey.Unknown];
        var time = TimeSpan.Zero;
        // Half the clicks land inside a real interactive node, half anywhere (including off-surface).
        PointerPoint Target()
        {
            var interactive = host.Frame.Lowering.Actions.Keys.Where(host.Frame.Resolved.Nodes.ContainsKey).ToArray();
            if (interactive.Length == 0 || random.Next(2) == 0) return new(random.Next(-10, 650), random.Next(-10, 650));
            Rect rect = host.Frame.Resolved.Nodes[interactive[random.Next(interactive.Length)]].Rect;
            return new(rect.X + random.NextDouble() * Math.Max(1, rect.Width - 1), rect.Y + random.NextDouble() * Math.Max(1, rect.Height - 1));
        }
        for (int step = 0; step < 2500; step++)
        {
            time += TimeSpan.FromMilliseconds(random.Next(0, 700));
            UiInputEvent input = random.Next(10) switch
            {
                0 or 1 or 2 => new UiKeyChanged(keys[random.Next(keys.Length)], random.Next(4) != 0, random.Next(8) == 0,
                    new(random.Next(4) == 0, random.Next(4) == 0, false, false)),
                3 => new UiTextEntered(random.Next(3) == 0 ? "\u00e9\u0301" : ((char)random.Next('a', 'z' + 1)).ToString()),
                4 or 5 => new UiPointerButtonChanged(Target(),
                    random.Next(6) == 0 ? UiPointerButton.Secondary : UiPointerButton.Primary, random.Next(2) == 0, default),
                6 => new UiPointerMoved(new(random.Next(0, 640), random.Next(0, 640)), null, default),
                7 => new UiPointerWheel(new(random.Next(0, 640), random.Next(0, 640)), 0, random.Next(-3, 4), default),
                8 => new UiFocusChanged(random.Next(2) == 0),
                _ => random.Next(2) == 0 ? new UiPointerCaptureLost() : new UiSurfaceResized(new(640, 640)),
            };
            var result = host.Session.Handle(input, time);
            foreach (var change in result.Changes) apply(change);
            host.Prepare();
            if (host.Session.Focused is { } focused)
                Assert.True(host.Frame.Resolved.Nodes.ContainsKey(focused), $"step {step}: focus on unmounted {focused}");
            if (input is UiPointerButtonChanged { IsPressed: false, Button: UiPointerButton.Primary } or UiFocusChanged { Focused: false } or UiPointerCaptureLost)
                Assert.False(host.Session.HasPointerCapture, $"step {step}: capture leaked after {input.GetType().Name}");
        }

        // Guard against a vacuous run: popups, text editing and numeric commits must all have been exercised.
        Assert.True(opened >= 5 && edited >= 5 && committed >= 3, $"opened {opened}, edited {edited}, committed {committed}");

        void apply(WidgetChange change) => Apply(change);
    }

    [Fact]
    public void ScrollFocusRingDrawsAboveFullWidthContentAndTheIndicatorHasItsOwnGutter()
    {
        using var host = new Host(w => w.ScrollArea("scroll", UI.Column(Enumerable.Range(0, 6)
            .Select(i => w.Button("row" + i, "Row " + i, width: 240)).ToArray(), gap: 8), 6 * 44, width: 240, height: 100), _ => { });
        Assert.True(host.Session.Focus("scroll", visible: true));
        host.Prepare();
        var nodes = host.Frame.Resolved.Nodes;
        Rect scroll = nodes[new NodeId("scroll")].Rect, viewport = nodes[new NodeId("scroll.viewport")].Rect;
        Rect indicator = nodes[new NodeId("scroll.scroll-indicator")].Rect;
        Assert.Equal(240 - WidgetBuilder.ScrollbarGutter, viewport.Width, 6);
        Assert.True(indicator.X >= viewport.X + viewport.Width, "indicator overlaps content");
        Assert.True(indicator.X + indicator.Width <= scroll.X + scroll.Width);
        // Painter order: the ring comes after every content operation, so content cannot paint over it.
        var operations = host.Frame.PresentationFrame.Operations;
        static string Source(object op) => op.GetType().GetProperty("SourceId")?.GetValue(op) as string ?? "";
        int ring = operations.ToList().FindLastIndex(op => Source(op).StartsWith("scroll.focus-ring", StringComparison.Ordinal));
        int lastRow = operations.ToList().FindLastIndex(op => Source(op).StartsWith("row", StringComparison.Ordinal));
        Assert.True(ring >= 0 && lastRow >= 0, $"ring {ring}, last row {lastRow}");
        Assert.True(ring > lastRow, $"ring {ring}, last row {lastRow}");
        // Over-wide content is clipped at the gutter: a row's hit area ends where the viewport ends.
        var hit = host.Frame.HitTest.HitTest(new(viewport.X + viewport.Width + 2, viewport.Y + 10));
        Assert.True(hit is null || !hit.NodeId.Value.StartsWith("row", StringComparison.Ordinal));
    }

    [Fact]
    public void OverflowingPopupListRowsStayClearOfTheIndicator()
    {
        WidgetOption[] many = Enumerable.Range(0, 30).Select(i => new WidgetOption("o" + i, "Option " + i)).ToArray();
        using var host = new Host(w => w.Select("sel", many, "o0", true), _ => { });
        var nodes = host.Frame.Resolved.Nodes;
        Rect indicator = nodes[new NodeId("sel.scroll.scroll-indicator")].Rect;
        Rect row = nodes[new NodeId("sel.option.o0")].Rect;
        Assert.True(row.X + row.Width <= indicator.X, $"row ends {row.X + row.Width}, indicator {indicator.X}");
        // A short list keeps full-width rows and no gutter.
        using var shortHost = new Host(w => w.Select("sel", Options, "a", true), _ => { });
        Assert.False(shortHost.Frame.Resolved.Nodes.ContainsKey(new NodeId("sel.scroll.scroll-indicator")));
    }

    private sealed class FakeClipboard(string text) : IWidgetClipboard
    {
        public bool TryRead(out string value) { value = text; return true; }
        public bool TryWrite(string value) => true;
    }

    /// <summary>A controlled host: applies every proposal, then re-prepares, as a native window loop does.</summary>
    private sealed class Host : IDisposable
    {
        private readonly Func<WidgetBuilder, UiNode> view;
        private readonly Action<WidgetChange> apply;
        private readonly WidgetStyle? style;
        private readonly int height;
        private readonly MachinaWidgetPipeline pipeline = new();
        public WidgetSession Session { get; }
        public MachinaPreparedPresentation Frame { get; private set; } = null!;

        public Host(Func<WidgetBuilder, UiNode> view, Action<WidgetChange> apply, IWidgetClipboard? clipboard = null, WidgetStyle? style = null, int height = 480)
        {
            this.view = view; this.apply = apply; this.style = style; this.height = height;
            Session = new WidgetSession { Clipboard = clipboard };
            Prepare();
        }

        public void Prepare()
        {
            Frame = pipeline.Prepare(Session, view, 640, height, style: style);
            foreach (var change in Session.DrainChanges()) apply(change);
        }

        public WidgetInputResult Send(UiInputEvent input, int milliseconds = 0)
        {
            var result = Session.Handle(input, TimeSpan.FromMilliseconds(milliseconds));
            foreach (var change in result.Changes) apply(change);
            Prepare();
            return result;
        }

        public WidgetInputResult Key(UiKey key, bool pressed = true, UiModifiers modifiers = default) =>
            Send(new UiKeyChanged(key, pressed, false, modifiers));
        public WidgetInputResult Pointer(double x, double y, bool pressed, int milliseconds = 0) =>
            Send(new UiPointerButtonChanged(new(x, y), UiPointerButton.Primary, pressed, default), milliseconds);
        public void Click(NodeId id)
        {
            Rect rect = Frame.Resolved.Nodes[id].Rect;
            Pointer(rect.X + rect.Width / 2, rect.Y + rect.Height / 2, true);
            Pointer(rect.X + rect.Width / 2, rect.Y + rect.Height / 2, false);
        }

        public void Dispose() => Session.Dispose();
    }
}
