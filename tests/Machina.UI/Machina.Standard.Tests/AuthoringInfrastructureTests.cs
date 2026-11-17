using System.Collections.Immutable;
using Machina.Core.Authoring;
using Machina.Core.Measurement;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Pipeline;
using Machina.Runtime.Input;
using Machina.Standard.Authoring;
using Machina.Standard.Widgets;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class AuthoringInfrastructureTests
{
    private static readonly WidgetOption[] Options = [new("a", "Alpha"), new("b", "Beta"), new("c", "Charlie")];

    private sealed class Model
    {
        public bool Audio;
        public string Name = "clip";
        public string? Format = "a";
        public bool FormatOpen;
        public bool MenuOpen = true;
        public ImmutableArray<string> Flags = [];
        public List<string> Invoked = [];
    }

    [Fact]
    public void BoundWidgetsApplyTheirOwnProposalsAndLeaveUnboundChangesToTheHost()
    {
        var m = new Model();
        bool unbound = false;
        using var host = new Host(w => UI.Column([
            w.Checkbox("audio", "Audio", Bind.Value(() => m.Audio, v => m.Audio = v)),
            w.Input("name", Bind.Value(() => m.Name, v => m.Name = v), "Name"),
            w.CheckboxGroup("flags", Options, Bind.Value(() => m.Flags, v => m.Flags = v)),
            w.Toolbar("tools", [new("cut", "Cut"), new("copy", "Copy")], key => m.Invoked.Add("tool:" + key)),
            w.Button("go", "Go", () => m.Invoked.Add("go")),
            w.Checkbox("plain", "Plain", unbound)], gap: 4), height: 700);
        host.Click("audio");
        Assert.True(m.Audio);
        host.Session.Focus("name");
        host.Text("s");
        Assert.Equal("clips", m.Name);
        host.Click("flags.c");
        Assert.Equal(["c"], m.Flags.ToArray());
        host.Click("tools.copy");
        host.Click("go");
        Assert.Equal(["tool:copy", "go"], m.Invoked.ToArray());
        // An unbound widget's proposal is returned, not silently dropped.
        var rest = host.Click("plain");
        var change = Assert.IsType<WidgetCheckedChanged>(Assert.Single(rest));
        Assert.Equal(new NodeId("plain"), change.Target);
    }

    [Fact]
    public void SelectAndMenuBindingsDriveOpenStateAndSelection()
    {
        var m = new Model();
        using var host = new Host(w => UI.Column([
            w.Select("format", Options, Bind.Value(() => m.Format, v => m.Format = v), Bind.Value(() => m.FormatOpen, v => m.FormatOpen = v)),
            w.Menu("menu", "File", [new("open", "Open"), new("save", "Save")], Bind.Value(() => m.MenuOpen, v => m.MenuOpen = v),
                key => m.Invoked.Add("menu:" + key))], gap: 4));
        // The menu starts open (controlled); choosing an item invokes and closes it through the binding.
        host.Click("menu.option.save");
        Assert.Equal(["menu:save"], m.Invoked.ToArray());
        Assert.False(m.MenuOpen);
        host.Click("format.trigger");
        Assert.True(m.FormatOpen);
        host.Click("format.option.c");
        Assert.Equal("c", m.Format);
        Assert.False(m.FormatOpen);
    }

    [Fact]
    public void EditBindingRoutesSliderPreviewCommitAndCancelSeparately()
    {
        decimal committed = 20, shown = 20;
        var log = new List<string>();
        using var host = new Host(w => w.Slider("gain", Bind.Edit(() => shown,
            preview: v => { shown = v; log.Add($"preview {v}"); },
            commit: v => { committed = v; shown = v; log.Add($"commit {v}"); },
            cancel: v => { shown = v; log.Add($"cancel {v}"); }), "Gain", width: 216));
        Rect box = host.Frame.Resolved.Nodes[new NodeId("gain")].Rect;
        double X(decimal v) => box.X + 8 + (double)v / 100 * (box.Width - 16);
        host.Pointer(X(50), box.Y + 10, true);
        host.Move(X(70), box.Y + 10);
        host.Key(UiKey.Escape);
        Assert.Equal(20, shown);
        Assert.Equal(20, committed);
        host.Pointer(X(40), box.Y + 10, true);
        host.Pointer(X(60), box.Y + 10, false);
        Assert.Equal(60, committed);
        Assert.Equal(["preview 50", "preview 70", "cancel 20", "preview 40", "commit 60"], log.ToArray());
    }

    [Fact]
    public void ComboboxBindingKeepsQueryAndSelectionSeparate()
    {
        string query = "";
        string? selected = null;
        bool open = false;
        using var host = new Host(w => w.Combobox("combo", Options, Bind.Value(() => selected, v => selected = v),
            Bind.Value(() => query, v => query = v), Bind.Value(() => open, v => open = v), "Asset"));
        host.Session.Focus("combo.input");
        host.Text("ar");
        Assert.Equal("ar", query);
        Assert.True(open);
        host.Key(UiKey.ArrowDown);
        host.Key(UiKey.Enter);
        Assert.Equal("c", selected);
        Assert.Equal("ar", query);
        Assert.False(open);
    }

    [Fact]
    public void TextLineEllipsizesNeverClipsVerticallyAndCanRefuseToTruncate()
    {
        var measurer = DeterministicTextMeasurer.Instance;
        var style = new TextStyle(Size: TextSize.Sm);
        const string text = "A considerably long label for a narrow box";
        double lineHeight = measurer.MeasureText(text, style).Height;
        var line = StandardUI.TextLine(text, 80, 4, measurer);
        Assert.Equal(80, line.Width);
        Assert.True(line.Height >= lineHeight);
        var shown = Assert.IsType<TextNode>(line.Child);
        Assert.EndsWith("...", shown.Text);
        Assert.True(measurer.MeasureText(shown.Text, style).Width <= 80);
        Assert.Equal(text, Assert.IsType<TextNode>(StandardUI.TextLine(text, 80, 20, measurer, overflow: TextOverflow.Clip).Child).Text);
        Assert.Throws<ArgumentException>(() => StandardUI.TextLine(text, 80, 20, measurer, overflow: TextOverflow.Throw));
        Assert.Equal("fits", Assert.IsType<TextNode>(StandardUI.TextLine("fits", 400, 20, measurer).Child).Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(37)]
    [InlineData(90)]
    [InlineData(500)]
    public void EllipsizeFindsTheLongestFittingGraphemePrefix(double width)
    {
        var measurer = DeterministicTextMeasurer.Instance;
        var style = new TextStyle(Size: TextSize.Sm);
        const string text = "Café au lait, piña colada";
        string result = TextFit.Ellipsize(text, width, style, measurer);
        Assert.True(result.Length == 0 || measurer.MeasureText(result, style).Width <= width);
        if (result.EndsWith("...", StringComparison.Ordinal) && result.Length > 3)
        {
            // The cut lands on a grapheme boundary (never between "e" and its combining acute) ...
            int kept = result.Length - 3;
            var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(text);
            Assert.Contains(boundaries, b => text[..b].TrimEnd().Length == kept);
            // ... and is maximal: keeping one more grapheme would not fit.
            int next = boundaries.FirstOrDefault(b => text[..b].TrimEnd().Length > kept);
            if (next > 0) Assert.True(measurer.MeasureText(text[..next].TrimEnd() + "...", style).Width > width);
        }
        if (measurer.MeasureText(text, style).Width <= width) Assert.Equal(text, result);
    }

    [Fact]
    public void FlowPlacesExplicitlyAndDeclaresItsOverflowPolicy()
    {
        double[] widths = [40, 40, 40, 40];
        var drop = Flow.Horizontal(130, widths, gap: 4);
        Assert.Equal(3, drop.Slots.Length);
        Assert.Equal(1, drop.Dropped);
        Assert.Equal([0d, 44d, 88d], drop.Slots.Select(s => s.X).ToArray());
        var wrap = Flow.Horizontal(130, widths, gap: 4, overflow: FlowOverflow.Wrap, rowHeight: 28, rowGap: 4);
        Assert.Equal(0, wrap.Dropped);
        Assert.Equal(2, wrap.Rows);
        Assert.Equal((1, 0d, 32d), (wrap.Slots[3].Row, wrap.Slots[3].X, wrap.Slots[3].Y));
        Assert.Equal(60, wrap.Height);
        var grouped = Flow.Horizontal(500, widths, gap: 2, groupBreaks: new HashSet<int> { 2 }, groupGap: 10);
        Assert.Equal([0d, 42d, 94d, 136d], grouped.Slots.Select(s => s.X).ToArray());
        Assert.Throws<InvalidOperationException>(() => Flow.Horizontal(100, widths, gap: 4, overflow: FlowOverflow.Throw));
        // An item wider than the whole row cannot be placed even when wrapping.
        var oversize = Flow.Horizontal(30, [40d], overflow: FlowOverflow.Wrap);
        Assert.Empty(oversize.Slots);
        Assert.Equal(1, oversize.Dropped);
    }

    [Fact]
    public void LayoutDumpIsDeterministicAndShowsRolesActionsAndClips()
    {
        using var host = new Host(w => UI.Column([
            w.Checkbox("audio", "Audio", false),
            w.ScrollArea("list", UI.Column([w.Button("row", "Row")]), 400, height: 100)], gap: 4));
        string first = MachinaLayoutDump.Format(host.Frame), second = MachinaLayoutDump.Format(host.Frame);
        Assert.Equal(first, second);
        Assert.Contains("audio 0,0 240x36 role=Checkbox \"Audio\" focusable action=audio", first, StringComparison.Ordinal);
        Assert.Contains("list.viewport", first, StringComparison.Ordinal);
        Assert.Contains(" clip", first, StringComparison.Ordinal);
        // Compact mode drops only anonymous nodes without semantics or actions; it never drops a node with a role.
        string full = MachinaLayoutDump.Format(host.Frame, compact: false);
        int Anonymous(string dump) => dump.Split('\n').Count(line => line.TrimStart().StartsWith("ui_", StringComparison.Ordinal));
        Assert.True(Anonymous(first) < Anonymous(full));
        Assert.All(first.Split('\n').Where(line => line.TrimStart().StartsWith("ui_", StringComparison.Ordinal)),
            line => Assert.True(line.Contains(" role=", StringComparison.Ordinal) || line.Contains(" action=", StringComparison.Ordinal), line));
    }

    private sealed class Host : IDisposable
    {
        private readonly Func<WidgetBuilder, UiNode> view;
        private readonly int height;
        private readonly MachinaWidgetPipeline pipeline = new();
        public WidgetSession Session { get; } = new();
        public MachinaPreparedPresentation Frame { get; private set; } = null!;
        public Host(Func<WidgetBuilder, UiNode> view, int height = 480) { this.view = view; this.height = height; Prepare(); }
        private ImmutableArray<WidgetChange> Prepare()
        {
            Frame = pipeline.Prepare(Session, view, 640, height);
            return Session.Apply(Session.DrainChanges());
        }
        public ImmutableArray<WidgetChange> Send(UiInputEvent input)
        {
            var rest = Session.Apply(Session.Handle(input).Changes);
            return rest.AddRange(Prepare());
        }
        public ImmutableArray<WidgetChange> Key(UiKey key) => Send(new UiKeyChanged(key, true, false, default));
        public ImmutableArray<WidgetChange> Text(string text) => Send(new UiTextEntered(text));
        public ImmutableArray<WidgetChange> Pointer(double x, double y, bool pressed) =>
            Send(new UiPointerButtonChanged(new(x, y), UiPointerButton.Primary, pressed, default));
        public ImmutableArray<WidgetChange> Move(double x, double y) => Send(new UiPointerMoved(new(x, y), null, default));
        public ImmutableArray<WidgetChange> Click(NodeId id)
        {
            Rect rect = Frame.Resolved.Nodes[id].Rect;
            Pointer(rect.X + rect.Width / 2, rect.Y + rect.Height / 2, true);
            return Pointer(rect.X + rect.Width / 2, rect.Y + rect.Height / 2, false);
        }
        public void Dispose() => Session.Dispose();
    }
}
