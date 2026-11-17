using System.Collections.Immutable;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;
using Machina.Pipeline;
using Machina.Runtime.Input;
using Machina.Standard.Authoring;
using Machina.Standard.Widgets;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class WidgetInteractionTests
{
    [Fact]
    public void CheckboxSpaceActivatesOnceOnReleaseAndReturnsAControlledProposal()
    {
        using var fixture = new Fixture(w => w.Checkbox("check", "Enabled", false));
        Assert.True(fixture.Session.Focus("check"));
        Assert.Empty(fixture.Key(UiKey.Space).Changes);
        var change = Assert.IsType<WidgetCheckedChanged>(Assert.Single(fixture.Key(UiKey.Space, false).Changes));
        Assert.True(change.Checked);
        Assert.Equal(new NodeId("check"), change.Target);
        Assert.Empty(fixture.Key(UiKey.Space, false).Changes);
        Assert.False(fixture.Frame.Lowering.Semantics[new NodeId("check")].Checked);
    }

    [Fact]
    public void DisabledWidgetsOccludeAndDoNotActivateTheControlUnderneath()
    {
        using var fixture = new Fixture(w => UI.Layer(children:
        [w.Button("under", "Under"), w.Button("disabled", "Disabled", disabled: true)]));
        var result = fixture.Click("disabled");
        Assert.True(result.Handled);
        Assert.Empty(result.Changes);
        Assert.Null(fixture.Session.Focused);
    }

    [Theory]
    [InlineData(WidgetActivation.Automatic, 1)]
    [InlineData(WidgetActivation.Manual, 0)]
    public void TabsDistinguishManualAndAutomaticKeyboardActivation(WidgetActivation activation, int expected)
    {
        using var fixture = new Fixture(w => w.Tabs("tabs",
            [new("a", "First", UI.Text("A")), new("b", "Disabled", UI.Text("B"), true), new("c", "Third", UI.Text("C"))], "a", activation: activation));
        fixture.Session.Focus("tabs.tabs.a");
        var moved = fixture.Key(UiKey.ArrowRight);
        Assert.Equal(new NodeId("tabs.tabs.c"), fixture.Session.Focused);
        Assert.Equal(expected, moved.Changes.Length);
        if (activation == WidgetActivation.Manual)
            Assert.Equal("c", Assert.IsType<WidgetSelectionChanged>(Assert.Single(fixture.Key(UiKey.Enter).Changes)).Keys.Single());
    }

    [Fact]
    public void RadioNavigationSkipsDisabledAndLoopsWithOneTabStop()
    {
        using var fixture = new Fixture(w => UI.Column([
            w.RadioGroup("radio", Options, "a"), w.Button("after", "After")]));
        fixture.Key(UiKey.Tab);
        Assert.Equal(new NodeId("radio.a"), fixture.Session.Focused);
        var change = Assert.IsType<WidgetSelectionChanged>(Assert.Single(fixture.Key(UiKey.ArrowUp).Changes));
        Assert.Equal("c", change.Keys.Single());
        fixture.Key(UiKey.Tab);
        Assert.Equal(new NodeId("after"), fixture.Session.Focused);
    }

    [Theory]
    [InlineData(WidgetDirection.LeftToRight, "c")]
    [InlineData(WidgetDirection.RightToLeft, "d")]
    public void HorizontalGroupsUseDirectionAndDisabledSkipping(WidgetDirection direction, string expected)
    {
        using var fixture = new Fixture(w => w.RadioGroup("radio", [.. Options, new("d", "Delta")], "a",
            orientation: WidgetOrientation.Horizontal, direction: direction));
        fixture.Session.Focus("radio.a");
        Assert.Equal(expected, Assert.IsType<WidgetSelectionChanged>(Assert.Single(fixture.Key(UiKey.ArrowRight).Changes)).Keys.Single());
    }

    [Fact]
    public void SliderHasPreviewCommitAndCancellationWithoutLosingExactDecimalValues()
    {
        const decimal minimum = 9_007_199_254_740_993m;
        using var fixture = new Fixture(w => w.Slider("slider", minimum, "Time", minimum, minimum + 10, 1, width: 200));
        var box = fixture.Frame.Resolved.Nodes[new NodeId("slider")].Rect;
        var press = fixture.Pointer(box.X + 100, box.Y + 10, true);
        Assert.True(press.CapturePointer);
        var preview = Assert.IsType<WidgetNumberChanged>(Assert.Single(press.Changes));
        Assert.Equal(minimum + 5, preview.Value);
        Assert.Equal(WidgetEditPhase.Preview, preview.Phase);
        var cancel = Assert.IsType<WidgetNumberChanged>(Assert.Single(fixture.Key(UiKey.Escape).Changes));
        Assert.Equal(minimum, cancel.Value);
        Assert.Equal(WidgetEditPhase.Cancel, cancel.Phase);
        Assert.False(fixture.Session.HasPointerCapture);
        fixture.Pointer(box.X + 100, box.Y + 10, true);
        var commit = Assert.IsType<WidgetNumberChanged>(Assert.Single(fixture.Pointer(box.X + 200, box.Y + 10, false).Changes));
        Assert.Equal(minimum + 10, commit.Value);
        Assert.Equal(WidgetEditPhase.Commit, commit.Phase);
    }

    [Fact]
    public void LosingFocusCancelsCaptureAndCannotLeaveSpaceArmed()
    {
        using var fixture = new Fixture(w => w.Slider("s", 30, "Value"));
        fixture.Pointer(100, 12, true);
        var result = fixture.Session.Handle(new UiFocusChanged(false));
        Assert.Equal(WidgetEditPhase.Cancel, Assert.IsType<WidgetNumberChanged>(Assert.Single(result.Changes)).Phase);
        Assert.False(fixture.Session.HasPointerCapture);
        // Like a native window, keyboard focus survives deactivation; only gestures and armed keys are dropped.
        Assert.Equal(new NodeId("s"), fixture.Session.Focused);
        fixture.Key(UiKey.Space);
        fixture.Session.Handle(new UiFocusChanged(false));
        Assert.Empty(fixture.Key(UiKey.Space, false).Changes);
    }

    [Fact]
    public void EditingDeletesWholeGraphemesAndPreservesTheAccessibleLabel()
    {
        string value = "A\U0001F680e\u0301";
        using var fixture = new Fixture(w => w.Input("name", value, "Display name"));
        fixture.Session.Focus("name");
        var deleted = Assert.IsType<WidgetTextChanged>(Assert.Single(fixture.Key(UiKey.Backspace).Changes));
        Assert.Equal("A\U0001F680", deleted.Text);
        value = deleted.Text;
        fixture.Prepare();
        deleted = Assert.IsType<WidgetTextChanged>(Assert.Single(fixture.Key(UiKey.Backspace).Changes));
        Assert.Equal("A", deleted.Text);
        Assert.Equal("Display name", fixture.Frame.Lowering.Semantics[new NodeId("name")].Label);
    }

    [Fact]
    public void ReadOnlyInputAllowsSelectionButRejectsTypingAndDeletion()
    {
        using var fixture = new Fixture(w => w.Input("name", "Read only", "Name", readOnly: true));
        fixture.Session.Focus("name");
        fixture.Key(UiKey.A, modifiers: new(true, false, false, false));
        Assert.Empty(fixture.Session.Handle(new UiTextEntered("changed")).Changes);
        Assert.Empty(fixture.Key(UiKey.Delete).Changes);
    }

    [Fact]
    public void InvalidNumberDraftReportsFailureAndDoesNotProposeANumber()
    {
        using var fixture = new Fixture(w => w.NumberField("number", 5, "Count", 0, 10));
        fixture.Session.Focus("number");
        fixture.Key(UiKey.A, modifiers: new(true, false, false, false));
        fixture.Session.Handle(new UiTextEntered("nope"));
        Assert.IsType<WidgetOperationFailed>(Assert.Single(fixture.Key(UiKey.Enter).Changes));
    }

    [Fact]
    public void SelectUsesCurrentTriggerGeometryAndRestoresFocusAfterSelection()
    {
        bool open = false;
        using var fixture = new Fixture(w => StandardUI.Place(w.Select("select", Options, "a", open), 250, 240, 240, 36), 520, 300);
        Assert.True(Assert.IsType<WidgetOpenChanged>(Assert.Single(fixture.Click("select.trigger").Changes)).Open);
        open = true;
        fixture.Prepare();
        var panel = fixture.Frame.Resolved.Nodes[new NodeId("select.popup")].Rect;
        Assert.InRange(panel.X, 0, 520 - panel.Width);
        Assert.True(panel.Y + panel.Height <= 240);
        Assert.Equal(new NodeId("select.option.a"), fixture.Session.Focused);
        fixture.Key(UiKey.End);
        var result = fixture.Key(UiKey.Enter);
        Assert.Equal("c", result.Changes.OfType<WidgetSelectionChanged>().Single().Keys.Single());
        Assert.False(result.Changes.OfType<WidgetOpenChanged>().Single().Open);
        open = false;
        fixture.Prepare();
        Assert.Equal(new NodeId("select.trigger"), fixture.Session.Focused);
    }

    [Fact]
    public void AlertDialogTrapsFocusAndIgnoresOutsideClick()
    {
        bool open = false;
        using var fixture = new Fixture(w => UI.Column([w.Button("background", "Background"),
            w.Dialog("dialog", "Open", "Confirm", w.Button("confirm", "Confirm"), open, alert: true)]));
        fixture.Click("dialog.trigger");
        open = true;
        fixture.Prepare();
        for (int index = 0; index < 5; index++)
        {
            fixture.Key(UiKey.Tab);
            Assert.Contains(fixture.Session.Focused!.Value, new NodeId[] { "dialog.close", "confirm" });
        }
        fixture.Pointer(1, 1, true);
        Assert.Empty(fixture.Pointer(1, 1, false).Changes);
        Assert.False(Assert.IsType<WidgetOpenChanged>(Assert.Single(fixture.Key(UiKey.Escape).Changes)).Open);
        open = false;
        fixture.Prepare();
        Assert.Equal(new NodeId("dialog.trigger"), fixture.Session.Focused);
    }

    [Fact]
    public void ClippedControlsCannotBeClickedOutsideTheirVisibleParent()
    {
        using var fixture = new Fixture(w => UI.Layer(children:
        [StandardUI.Place(UI.Rect(StandardUI.Place(w.Button("hidden", "Hidden"), 0, 40, 112, 36),
            style: new(ClipToBounds: true)), 0, 0, 150, 50)]));
        Assert.NotNull(fixture.Frame.HitTest.HitTest(new(20, 45)));
        Assert.Null(fixture.Frame.HitTest.HitTest(new(20, 60)));
        fixture.Pointer(20, 60, true);
        Assert.Empty(fixture.Pointer(20, 60, false).Changes);
    }

    [Theory]
    [InlineData(WidgetPopupSide.Bottom)]
    [InlineData(WidgetPopupSide.Top)]
    [InlineData(WidgetPopupSide.Left)]
    [InlineData(WidgetPopupSide.Right)]
    public void PopupPlacementClampsOversizedPanelsAtEveryEdge(WidgetPopupSide side)
    {
        Rect result = WidgetPopupPlacement.Place(new(390, 280, 20, 20), new(0, 0, 400, 300), 600, 500, side);
        Assert.Equal(new Rect(0, 0, 400, 300), result);
    }

    private static readonly WidgetOption[] Options = [new("a", "Alpha"), new("b", "Beta", true), new("c", "Charlie")];

    [Fact]
    public void PipelineAcceptsTheExistingSurfaceAuthoringConvention()
    {
        using var fixture = new Fixture(w => UI.Surface(id: "screen", children: [w.Button("ok", "OK")]));
        Assert.True(fixture.Frame.Resolved.Nodes.ContainsKey("screen"));
        Assert.IsType<WidgetInvoked>(Assert.Single(fixture.Click("ok").Changes));
    }

    [Fact]
    public void ComboboxFiltersWithoutMutatingQueryAndCommitsAnOptionByKey()
    {
        string query = "";
        bool open = false;
        using var fixture = new Fixture(w => w.Combobox("combo", Options, null, query, open, "Asset"));
        fixture.Session.Focus("combo.input");
        var typed = fixture.Session.Handle(new UiTextEntered("ar"));
        query = typed.Changes.OfType<WidgetTextChanged>().Single().Text;
        open = typed.Changes.OfType<WidgetOpenChanged>().Single().Open;
        fixture.Prepare();
        Assert.Equal(new NodeId("combo.input"), fixture.Session.Focused);
        Assert.False(fixture.Frame.Resolved.Nodes.ContainsKey("combo.option.a"));
        Assert.True(fixture.Frame.Resolved.Nodes.ContainsKey("combo.option.c"));
        fixture.Key(UiKey.ArrowDown);
        Assert.Equal(new NodeId("combo.option.c"), fixture.Session.Focused);
        var committed = fixture.Key(UiKey.Enter);
        Assert.Equal("c", committed.Changes.OfType<WidgetSelectionChanged>().Single().Keys.Single());
        Assert.False(committed.Changes.OfType<WidgetOpenChanged>().Single().Open);
        Assert.Equal("ar", query);
        open = false;
        fixture.Prepare();
        Assert.Equal(new NodeId("combo.input"), fixture.Session.Focused);
    }

    [Fact]
    public void ComboboxHandlesNoMatchesAndEscapeWithoutSelectingAnUnrelatedItem()
    {
        using var fixture = new Fixture(w => w.Combobox("combo", Options, null, "missing", true, "Asset"));
        Assert.True(fixture.Session.Focus("combo.input"));
        Assert.Empty(fixture.Key(UiKey.ArrowDown).Changes);
        Assert.Empty(fixture.Key(UiKey.Enter).Changes);
        Assert.False(Assert.IsType<WidgetOpenChanged>(Assert.Single(fixture.Key(UiKey.Escape).Changes)).Open);
    }

    [Fact]
    public void AccordionHeadersRemainIndividualTabStopsAndCheckboxGroupProposesMultipleValues()
    {
        using var fixture = new Fixture(w => UI.Column([
            w.Accordion("accordion", [new("a", "A", UI.Text("A")), new("b", "B", UI.Text("B"))], []),
            w.CheckboxGroup("checks", Options, ["a"])]));
        fixture.Session.Focus("accordion.a.trigger");
        fixture.Key(UiKey.Tab);
        Assert.Equal(new NodeId("accordion.b.trigger"), fixture.Session.Focused);
        fixture.Key(UiKey.Tab);
        Assert.Equal(new NodeId("checks.a"), fixture.Session.Focused);
        fixture.Key(UiKey.Tab);
        Assert.Equal(new NodeId("checks.c"), fixture.Session.Focused);
        Assert.Equal(new[] { "a", "c" }, fixture.Key(UiKey.Enter).Changes.OfType<WidgetSelectionChanged>().Single().Keys);
    }

    [Fact]
    public void SelectInsideDialogIsInTheModalFocusScopeAndRestoresItsTrigger()
    {
        bool open = false;
        using var fixture = new Fixture(w => w.Dialog("dialog", "Dialog", "Dialog",
            w.Select("select", Options, "a", open), true));
        fixture.Click("select.trigger");
        open = true;
        fixture.Prepare();
        Assert.Equal(new NodeId("select.option.a"), fixture.Session.Focused);
        fixture.Key(UiKey.End);
        Assert.Equal("c", fixture.Key(UiKey.Enter).Changes.OfType<WidgetSelectionChanged>().Single().Keys.Single());
        open = false;
        fixture.Prepare();
        Assert.Equal(new NodeId("select.trigger"), fixture.Session.Focused);
    }

    [Fact]
    public void TabDismissalMovesPastSelectWithoutRestoringThePreviousFocus()
    {
        bool open = false;
        using var fixture = new Fixture(w => UI.Column([w.Select("select", Options, "a", open), w.Button("after", "After")]));
        fixture.Click("select.trigger");
        open = true;
        fixture.Prepare();
        fixture.Key(UiKey.Tab);
        open = false;
        fixture.Prepare();
        Assert.Equal(new NodeId("after"), fixture.Session.Focused);
    }

    [Fact]
    public void LongSelectScrollsItsSelectedAndKeyboardFocusedOptionsIntoView()
    {
        var options = Enumerable.Range(0, 40).Select(index => new WidgetOption(index.ToString(), "Option " + index)).ToArray();
        using var fixture = new Fixture(w => w.Select("select", options, "30", true), 400, 240);
        Assert.Equal(new NodeId("select.option.30"), fixture.Session.Focused);
        AssertVisible("select.option.30");
        fixture.Key(UiKey.End);
        fixture.Prepare();
        AssertVisible("select.option.39");
        Assert.Equal("39", fixture.Key(UiKey.Enter).Changes.OfType<WidgetSelectionChanged>().Single().Keys.Single());

        void AssertVisible(NodeId id)
        {
            Rect item = fixture.Frame.Resolved.Nodes[id].Rect;
            Rect viewport = fixture.Frame.Resolved.Nodes[new NodeId("select.scroll")].Rect;
            Assert.True(item.Y >= viewport.Y && item.Y + item.Height <= viewport.Y + viewport.Height);
            Assert.Equal(id, fixture.Frame.HitTest.HitTest(new(item.X + 10, item.Y + 10))!.NodeId);
        }
    }

    [Fact]
    public void ScrollAreaConsumesWheelAndKeyboardWithoutMutatingApplicationState()
    {
        using var fixture = new Fixture(w => w.ScrollArea("scroll",
            UI.Column(Enumerable.Range(0, 20).Select(index => w.Button("row" + index, "Row " + index)).ToArray(), gap: 8),
            880, height: 150));
        fixture.Session.Focus("scroll");
        Assert.True(fixture.Key(UiKey.End).Handled);
        fixture.Prepare();
        double bottomY = fixture.Frame.Resolved.Nodes[new NodeId("row19")].Rect.Y;
        Assert.InRange(bottomY, 0, 150);
        var wheel = fixture.Session.Handle(new UiPointerWheel(new(20, 20), 0, 2, default));
        Assert.True(wheel.Handled);
        Assert.Empty(wheel.Changes);
        fixture.Prepare();
        Assert.True(fixture.Frame.Resolved.Nodes[new NodeId("row19")].Rect.Y > bottomY);
        fixture.Key(UiKey.Home);
        fixture.Prepare();
        Assert.Equal(0, fixture.Frame.Resolved.Nodes[new NodeId("row0")].Rect.Y);
    }

    [Fact]
    public void TooltipEscapeSuppressesReopeningUntilFocusLeaves()
    {
        using var fixture = new Fixture(w => UI.Column([w.Tooltip("tip", "Tip", "Explanation"), w.Button("after", "After")]));
        fixture.Session.Focus("tip.trigger");
        fixture.Prepare();
        Assert.True(fixture.Frame.Resolved.Nodes.ContainsKey("tip.popup"));
        fixture.Key(UiKey.Escape);
        fixture.Prepare();
        Assert.False(fixture.Frame.Resolved.Nodes.ContainsKey("tip.popup"));
        fixture.Session.Focus("after");
        fixture.Prepare();
        fixture.Session.Focus("tip.trigger");
        fixture.Prepare();
        Assert.True(fixture.Frame.Resolved.Nodes.ContainsKey("tip.popup"));
    }

    [Fact]
    public void ClosingNestedDialogsRestoresTheSurvivingParentTrigger()
    {
        bool parent = true, child = true;
        using var fixture = new Fixture(w => w.Dialog("parent", "Parent", "Parent",
            w.Dialog("child", "Child", "Child", w.Button("inside", "Inside"), child), parent));
        parent = child = false;
        fixture.Prepare();
        Assert.Equal(new NodeId("parent.trigger"), fixture.Session.Focused);
    }

    [Fact]
    public void InputPointerPositionsCaretAndDragSelectsText()
    {
        string value = "abcdef";
        using var fixture = new Fixture(w => w.Input("text", value, "Text"));
        fixture.Pointer(8, 15, true);
        fixture.Pointer(8, 15, false);
        var inserted = Assert.IsType<WidgetTextChanged>(Assert.Single(fixture.Session.Handle(new UiTextEntered("X")).Changes));
        Assert.Equal("Xabcdef", inserted.Text);
        value = inserted.Text;
        fixture.Prepare();
        fixture.Pointer(8, 15, true);
        fixture.Session.Handle(new UiPointerMoved(new(300, 15), new(8, 15), default));
        fixture.Pointer(300, 15, false);
        Assert.Equal("", Assert.IsType<WidgetTextChanged>(Assert.Single(fixture.Key(UiKey.Delete).Changes)).Text);
    }

    [Fact]
    public void InputLengthLimitPreservesTheSuffixAndDoesNotSplitSurrogates()
    {
        using var fixture = new Fixture(w => w.Input("text", "abcd", "Text", maximumLength: 5));
        fixture.Session.Focus("text");
        fixture.Key(UiKey.Home);
        var inserted = Assert.IsType<WidgetTextChanged>(Assert.Single(fixture.Session.Handle(new UiTextEntered("XYZ")).Changes));
        Assert.Equal("Xabcd", inserted.Text);
        fixture.Key(UiKey.A, modifiers: new(true, false, false, false));
        Assert.Equal("\U0001F680\U0001F680", Assert.IsType<WidgetTextChanged>(Assert.Single(
            fixture.Session.Handle(new UiTextEntered("\U0001F680\U0001F680\U0001F680")).Changes)).Text);
    }

    [Fact]
    public void TextUndoRedoSurvivesControlledRebuildAndNewTypingClearsRedo()
    {
        string value = "old";
        using var fixture = new Fixture(w => w.Input("text", value, "Text"));
        fixture.Session.Focus("text");
        fixture.Key(UiKey.A, modifiers: new(true, false, false, false));
        value = fixture.Session.Handle(new UiTextEntered("new")).Changes.OfType<WidgetTextChanged>().Single().Text;
        fixture.Prepare();
        value = fixture.Key(UiKey.Z, modifiers: new(true, false, false, false)).Changes.OfType<WidgetTextChanged>().Single().Text;
        Assert.Equal("old", value);
        fixture.Prepare();
        value = fixture.Key(UiKey.Z, modifiers: new(true, true, false, false)).Changes.OfType<WidgetTextChanged>().Single().Text;
        Assert.Equal("new", value);
        fixture.Prepare();
        fixture.Key(UiKey.Z, modifiers: new(true, false, false, false));
        fixture.Key(UiKey.End);
        fixture.Session.Handle(new UiTextEntered("!"));
        Assert.Equal("old!", fixture.Key(UiKey.Y, modifiers: new(true, false, false, false)).Changes.OfType<WidgetTextChanged>().Single().Text);
    }

    [Fact]
    public void DecimalExtremeStepDoesNotOverflowAndMenuHasMenuSemantics()
    {
        using var fixture = new Fixture(w => UI.Column([
            w.Slider("s", 0, "Value", 0, decimal.MaxValue, decimal.MaxValue),
            w.Menu("menu", "Menu", Options, true)]));
        // A menu has real item semantics, not list-box semantics.
        Assert.Equal(Machina.Core.Semantics.UiRole.MenuItem, fixture.Frame.Lowering.Semantics[new NodeId("menu.option.a")].Role);
        fixture.Key(UiKey.Escape);
        // Use a separate view because the controlled menu remains open above.
        using var numeric = new Fixture(w => w.Slider("s", 0, "Value", 0, decimal.MaxValue, decimal.MaxValue));
        numeric.Session.Focus("s");
        Assert.Equal(decimal.MaxValue, numeric.Key(UiKey.PageUp).Changes.OfType<WidgetNumberChanged>().Single().Value);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Func<WidgetBuilder, UiNode> declaration;
        private readonly int width;
        private readonly int height;
        public Fixture(Func<WidgetBuilder, UiNode> declaration, int width = 640, int height = 480)
        {
            this.declaration = declaration;
            this.width = width;
            this.height = height;
            Prepare();
        }
        public WidgetSession Session { get; } = new();
        private readonly MachinaWidgetPipeline pipeline = new();
        private MachinaPreparedPresentation? frame;
        public MachinaPreparedPresentation Frame => frame ?? Prepare();
        public MachinaPreparedPresentation Prepare() => frame = pipeline.Prepare(Session, declaration, width, height);
        public WidgetInputResult Key(UiKey key, bool pressed = true, UiModifiers modifiers = default) =>
            Session.Handle(new UiKeyChanged(key, pressed, false, modifiers));
        public WidgetInputResult Pointer(double x, double y, bool pressed) =>
            Session.Handle(new UiPointerButtonChanged(new(x, y), UiPointerButton.Primary, pressed, default));
        public WidgetInputResult Click(NodeId id)
        {
            Rect rect = Frame.Resolved.Nodes[id].Rect;
            Pointer(rect.X + rect.Width / 2, rect.Y + rect.Height / 2, true);
            return Pointer(rect.X + rect.Width / 2, rect.Y + rect.Height / 2, false);
        }
        public void Dispose() => Session.Dispose();
    }
}
