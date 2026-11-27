# Machina.UI authoring guide

Build a first C# screen, prepare its layout and presentation, then choose the host that displays it. This guide explains the current authoring workflow; linked references cover deeper behavior.

## Choose an authoring shape

Use `UiDocument` and `UiRow` when a screen should read as a flat blueprint with explicit row IDs, parent IDs, frames, ordering, and optional arrangers. `Row.Root`, `Row.Absolute`, `Row.Anchor`, `Row.Fixed`, and `Row.Fill` construct common rows. A row may host a component in its `Component` field.

Use the `UI` builder when a local component or tree-shaped declaration is clearer. `UI.Rect`, `UI.Text`, `UI.Button`, `UI.Row`, `UI.Column`, `UI.Stack`, `UI.Grid`, `UI.Surface`, `UI.Layer`, `UI.At`, and `UI.Anchor` build `UiNode` declarations. These declarations lower to flat layout rows; the tree is an authoring form, while layout consumes the rows.

Both entry points are accepted by `MachinaPresentationPipeline.Prepare`. The pipeline lowers or compiles the declaration, resolves geometry for the supplied viewport dimensions, builds hit testing and a presentation frame, and returns `MachinaPreparedPresentation`. It prepares backend-neutral presentation intent; a host still owns windowing, platform input loops, and realization.

Use a .NET 10 console project referencing `Machina.Core`, `Machina.Standard`,
and `Machina.Pipeline` (the projects under `src/Machina.UI`). In a consumer project,
add `ProjectReference` entries to their `.csproj` files, then use this complete
`Program.cs`:

```csharp
using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Pipeline;
using Machina.Standard.Authoring;

UiDocument screen = UiDocument.Create(
[
    Row.Root("screen"),
    Row.Absolute(
        id: "settings-card-slot",
        parent: "screen",
        x: 24,
        y: 24,
        width: 480,
        height: 280) with
    {
        Component = StandardUI.Card(
            id: "settings-card",
            children: [UI.Text("Settings")])
    }
]);

MachinaPreparedPresentation frame = new MachinaPresentationPipeline()
    .Prepare(screen, width: 1280, height: 720);

Console.WriteLine("Prepared settings screen.");
```

Run `dotnet run --project <your-project>`; it prints `Prepared settings screen.`
after preparation succeeds. This example prepares a frame in memory. To display
and interact with it, continue to the host section below and the linked samples.

`Row.Absolute` sets placement; the immutable `with` update supplies its hosted
`Component`. `Row.Root` and `Row.Anchor` also accept a component directly.

The row helper signatures and `Prepare` overloads are in [Row.cs](../../src/Machina.UI/Machina.Core/Flat/Row.cs), [UiDocument.cs](../../src/Machina.UI/Machina.Core/Flat/UiDocument.cs), and [MachinaPresentationPipeline.cs](../../src/Machina.UI/Machina.Pipeline/MachinaPresentationPipeline.cs). Tree builders are in [UI.cs](../../src/Machina.UI/Machina.Core/Authoring/UI.cs); lowering is in [UiLowerer.cs](../../src/Machina.UI/Machina.Core/Lowering/UiLowerer.cs).

## Placement and flow

- Use `Row.Absolute` or `UI.At` for explicit placement, and anchor frames when a child should attach to parent edges.
- Use stack arrangers (`UI.Row`, `UI.Column`, `UI.Stack`, `UI.HStack`, `UI.VStack`) for ordered flow. Explicit `Fixed`, `Fill`, and `Auto` stack items describe sizing along the stack's main axis.
- Use `UI.Grid` with fixed or fill tracks for row/column placement.
- A `Surface` creates a root layer and a `Layer` creates a nested layer. Children are not automatically arranged as a stack; give them placement or an arranger.
- Component style padding is paint metadata. When a component hosts child content, its implementation needs an explicit content region; style padding does not move child rows.

Stacking and grid behavior are resolved in [LayoutDocumentResolver.cs](../../src/Machina.UI/Machina.Layout/Resolving/LayoutDocumentResolver.cs); frame rules are resolved in [FrameResolver.cs](../../src/Machina.UI/Machina.Layout/Resolving/FrameResolver.cs). The source-backed architecture notes are [flat authoring](architecture/machina-flat-authoring.md) and [authoring model history](architecture/machina-authoring-model.md).

## Standard components, styles, and text

Use `StandardUI` for the standard component surface: button, card, badge, label, text block, field, input, checkbox, switch, and separator. Pass a `StandardTheme` explicitly when a screen or component owns theme selection. `StandardView` provides lightweight row metadata for flat composition; it is not a tree constructor.

Use Standard rich text (`Text.Plain`, `Text.Markup`, and related builders) through `StandardUI.TextBlock` when text needs paragraphs, inline emphasis, lists, or links. The general layout system assigns the text box; Machina Standard Text lays out content inside that box. The legacy `UI.Text` remains a primitive text node and does not turn general layout into a text-measurement system.

Sources: [StandardUI.cs](../../src/Machina.UI/Machina.Standard/Authoring/StandardUI.cs), [StandardView.cs](../../src/Machina.UI/Machina.Standard/Authoring/StandardView.cs), [StandardTheme.cs](../../src/Machina.UI/Machina.Standard/Theme/StandardTheme.cs), and [MachinaTextLayoutEngine.cs](../../src/Machina.UI/Machina.Standard/Text/MachinaTextLayoutEngine.cs). Tests: [Standard tests](../../tests/Machina.UI/Machina.Standard.Tests/).

### Positioned controls and measured text

Use the positioned `StandardUI.Button` and `StandardUI.Label` overloads for explicit
screen layouts. They own the placement wrapper, standard semantics, and control
internals. `StandardUI.Place` positions any other component, including cards,
checkboxes, and stacks. Keep IDs tied to semantic identity, independently of position.

`StandardUI.Frame` groups children in parent-relative coordinates. `StandardUI.Panel`
adds a themed background and border to that frame; child positions start at its
top-left corner with no implicit padding. Use `Card` when its inset and stack
composition are appropriate. Define product colors and component styles once in a
product-owned palette built on `StandardTheme` and the standard style records.

```csharp
var body = StandardUI.Paragraph("Text wraps at the measured width.", 300, id: "body");
UiNode screen = UI.Surface(children:
[
    StandardUI.Panel(250, 150, 340, 200, id: "dialogue", children:
    [
        StandardUI.Label("Speaker", 20, 16, 300, id: "speaker"),
        StandardUI.Place(body, 20, 50),
        StandardUI.Button("Continue", 20, 140, 160, id: "continue",
            action: Machina.Core.Actions.UiAction.Named("continue")),
    ]),
]);
```

`Paragraph` is plain text with a fixed width and a measured height. Its returned
`RectNode.Width` and `Height` can size an enclosing panel or place subsequent content;
`Place(paragraph, x, y)` uses those dimensions directly. Paragraphs also work as
auto-sized children in a `UI.Column`. By default they use deterministic bitmap metrics.
Native font hosts should pass their `IMachinaTextMeasurer` to `Paragraph`; the same
measurer is retained for rich-text presentation, so wrapping, run positions and
content height match the renderer. TinyFarm supplies its MSDF atlas metrics here.
For markup, use `MachinaTextLayoutEngine.Measure(spec, width, measurer)` and pass
both the resulting height and the same measurer to `StandardUI.TextBlock`.

TinyFarm's HUD, journal, dialogue and menus, and Aurelian's `GameMenuTemplate` use
this surface.

## Bound widgets, measured text, flow and layout dumps

These helpers remove the glue code where bugs collected, without hiding layout. Positions stay explicit numbers.

**Bound widgets.** Declare a controlled value and its destination once instead of passing a value and then routing
`WidgetChange` records by target ID in a host switch:

```csharp
w.Checkbox("export.audio", "Include audio", Bind.Value(() => model.Audio, v => model.Audio = v));
w.Slider("clip.opacity", Bind.Edit(() => opacity,
    preview: v => opacity = v,                                   // transient, no undo entry
    commit: v => engine.Commit(new SetOpacity(clipId, v))), "Opacity", 0, 1, .01m);
w.Menu("file", "File", items, Bind.Value(() => fileOpen, v => fileOpen = v), key => Run(key));

var unbound = session.Apply(session.Handle(input).Changes);    // and session.Apply(session.DrainChanges())
```

Overloads are typed (a checkbox takes `Bound<bool>`, a slider `Bound<decimal>`, a select `Bound<string?>` plus a
`Bound<bool>` for open). `Apply` returns everything it did not consume: unbound widgets and `WidgetOperationFailed`.
A host that binds every widget can assert that nothing but tooltip dismissals comes back, which turns a forgotten
binding into a test failure (Valencia's widget gallery does this). Machina targets .NET 10, so these types do not use
C# 15 `closed` hierarchies or unions; applications on .NET 11 may use them for their own intent and command types.

**Measured text.** `StandardUI.TextLine(text, width, height, measurer, ...)` (or `WidgetBuilder.TextLine` with the
frame's measurer) aligns text inside the box with `alignX` and `alignY`. The defaults are left and vertically centred.
The box keeps the requested width and grows to at least the measured line height. Choose `TextOverflow.Ellipsis`,
`Clip`, or `Throw` for long labels. `TextFit.Ellipsize` is the shared trimming
routine, cutting only at grapheme boundaries. Use these instead of a raw `UI.Text` in a fixed row: an overflowing
`UI.Row` is a `LayoutError`, not a visual glitch.

**Flow.** `Flow.Horizontal(available, widths, gap, overflow, ...)` is a pure function from widths to slot positions
with a declared overflow policy (`Drop`, `Wrap`, `Throw`) and optional group gaps. It replaces `x += width + gap`
cursor walks. For explicitly bounded rows or columns, use `LinearFit.Place` from `Machina.Layout.Geometry`.
Supply each lane's origin and length along the layout axis. Use `trailingReserve` to leave room at the end of the
final lane, such as for an overflow button. The returned count gives the index of the first item that did not fit.

```csharp
var strips = LinearFit.Place([new(44), new(44), new(44), new(44)], [new(0, 169)]);
// Place the first three strips at 0, 44, and 88. The fourth stays in overflow.
```

**Layout dump.** `MachinaLayoutDump.Format(prepared)` renders the resolved layout as deterministic text: one line per
node with ID, rect, clip, role, label, action and flags, indented by depth in paint order. Compact mode omits
anonymous nodes without semantics or actions. Use it for golden layout tests and reviews.

## Preparation caching and widget lifetimes

For stable identity and action dispatch, use `UiIdFamily<TKey>`,
`UiAction<TMessage>`, and `UiActionFamily<TKey, TMessage>` from Machina.Core.
Compose child IDs with `NodeId.Child`; put domain IDs in immutable action messages
instead of concatenating them into command strings. `StandardUI.Button(text,
action, x, y, width, ...)` supplies an action-derived node ID by default, and
`UiTableRow.Id` accepts a typed `NodeId`. Declare typed widget slots with
`UiStateKey<T>`. See [typed identity and actions](architecture/machina-typed-identity-and-actions.md)
for examples, migration details and the external playtest lookup boundary.

Components own the IDs they derive from their root. For example, a button with ID `save` owns `save.label`
and `save.label-region`. Give authored siblings distinct IDs such as `save-hint`. The suffixes are available
under other IDs that do not derive those children. A `DuplicateUiNodeId` diagnostic reports the reused ID
and the location of both nodes in the declaration.

Use `MachinaPresentationCache.Prepare(declaration, width, height)` to reuse layout,
text presentation and hit testing for an unchanged declaration. Reconstruct the pure
declaration on each request; the cache compares values, including ordered children,
geometry, styles, semantics, actions, and standard rich-text metadata. Dimensions
and lowering options also participate. No separately maintained change key is needed.
Unknown rich-text payloads conservatively rebuild. This is whole-view caching.

Use a `Machina.Runtime.State.UiStateScope` for one screen or modal entry:

```csharp
using var scope = new Machina.Runtime.State.UiStateScope();
var search = scope.GetOrCreate("search", () => new Machina.Runtime.State.UiState<string>(""));
search.Value = "query";
```

Use stable `NodeId` values for slots. `UiStateLifetime.Scope` values survive
suspension; `UiStateLifetime.Activation` values are forgotten by `Suspend()` and
suit focus, presses, and scrollbar drags. Disposing a scope cancels its `Lifetime`
token and releases all values. The host must connect these operations to actual
screen transitions, including transitions between two render requests.

TinyFarm uses Dominatus HFSM frames to own these scopes. Push suspends the parent;
pop cancels the child and resumes the parent; a new entry gets new state. World
restore and shutdown discard the complete UI workflow. UI state is transient and
does not enter simulation saves or replay hashes. Machina itself has no Dominatus
dependency. See [screen state and invalidation](architecture/machina-screen-state-and-invalidation.md)
for the evaluation, lifetime policy, source paths and limits.

## Headless PNG previews

Reference [Machina.Testing](../../tests/Machina.UI/Machina.Testing/Machina.Testing.csproj) from your test project.
Render a tree declaration directly, or capture a prepared frame after a widget action such as opening a menu.

```csharp
using Machina.Testing;

var pixels = HeadlessPreview.WritePng(screenDeclaration, 800, 480, "artifacts/screen.png");
HeadlessPreview.WritePng(preparedFrame, "artifacts/menu-open.png");
```

The helper runs the Machina presentation pipeline and CPU reference rasterizer without a window or GPU.
The output uses reference bitmap glyphs. Use the native host to verify product font rendering.

## Follow the real sample and test paths

- The [Presenter sample](../../samples/Integrations/Machina.Presenter.Sample/) demonstrates a host with screen composition and presentation. Related presentation contracts are covered by [Machina.Presentation.Tests](../../tests/Machina.UI/Machina.Presentation.Tests/) and host integration tests under [Aurelian.Machina.Tests](../../tests/Integrations/Aurelian.Machina.Tests/).
- The [Component Gallery sample](../../samples/Machina.UI/Machina.ComponentGallery.Sample/) demonstrates standard components; its project tests live in [Machina.Standard.Tests](../../tests/Machina.UI/Machina.Standard.Tests/).
- The [Machina.UI solution](../../Machina.UI.slnx) is the regular UI validation entry point. The [slow solution](../../Machina.UI.Slow.slnx) includes diagnostics, samples, and font tooling.

For exact project and lane policy, follow [test-lane doctrine](../architecture/jtf-test-lane-doctrine.md). Historical milestone records remain available under [history](history/) for their original evidence and decisions.
