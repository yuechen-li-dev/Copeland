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

## Follow the real sample and test paths

- The [Presenter sample](../../samples/Integrations/Machina.Presenter.Sample/) demonstrates a host with screen composition and presentation. Related presentation contracts are covered by [Machina.Presentation.Tests](../../tests/Machina.UI/Machina.Presentation.Tests/) and host integration tests under [Aurelian.Machina.Tests](../../tests/Integrations/Aurelian.Machina.Tests/).
- The [Component Gallery sample](../../samples/Machina.UI/Machina.ComponentGallery.Sample/) demonstrates standard components; its project tests live in [Machina.Standard.Tests](../../tests/Machina.UI/Machina.Standard.Tests/).
- The [Machina.UI solution](../../Machina.UI.slnx) is the regular UI validation entry point. The [slow solution](../../Machina.UI.Slow.slnx) includes diagnostics, samples, and font tooling.

For exact project and lane policy, follow [test-lane doctrine](../architecture/jtf-test-lane-doctrine.md). Historical milestone records remain available under [history](history/) for their original evidence and decisions.
