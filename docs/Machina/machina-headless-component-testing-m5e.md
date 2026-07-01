# Machina Headless Component Testing (M5e)

M5e introduces reusable headless geometry harness helpers in `tests/Machina.Testing`.

## Pattern

1. Build a component or full document.
2. Resolve it through the real lowering/layout/hit-test path.
3. Assert resolved row rectangles and metadata.
4. Assert hit-test behavior from deterministic points.

## Example

```csharp
var result = GeometryHarness.ResolveComponent(
    StandardUI.Button("Save", id: "save", action: UiAction.Named("save")),
    width: 180,
    height: 30,
    hostId: "host");

result.AssertRect("host", 0, 0, 180, 30);
result.AssertHitActionInside("host/save", "save");
```

## Guidance for new components

Future components should include:
- geometry tests,
- metadata/style/semantics tests,
- hit-target tests,
- state-stability row-shape tests for stateful controls.

Headless geometry + metadata is primary validation. Manual GUI or screenshots are optional secondary confirmation.

- M5f update: presenter sample is the canonical reference app and is contract-tested in tests/Machina.Presenter.Sample.Tests (document shape, hosted component boundary, localized StandardUI internals, plain C# dispatch, theme propagation, and geometry/hit-target stability).


## M5g presenter visual regression audit
M5g requires converting presenter screenshot regressions into headless geometry/render-command tests. Verify UI.Text visibility by DrawTextCommand presence + visible text color + in-card rect; verify default button text-style size fits default shell; verify checkbox checked mark via explicit mark geometry and visible fill when checked (transparent when unchecked). Dynamic text fitting remains deferred.

## M6a Machina.Text boundary note

M6a establishes `Machina.Text` as a separate subsystem contract. Frame/stack/table layout still places component rectangles; `Machina.Text` will lay out text only inside those assigned boxes.

Wrap, overflow, leading, block/list spacing, and text alignment are text-domain primitives and must not be added to general layout semantics.

Headings remain a component/layout responsibility (for example title variant selection in `StandardUI.Card`), not a supported inline markup mechanism inside restricted Machina text source.

The current simple `UI.Text` path is transitional until `Machina.Text` parser/model/layout integration milestones (M6b+) are complete.
