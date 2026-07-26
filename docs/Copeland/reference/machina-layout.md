# Copeland Machina layout core

## Status

This document describes the implemented M1 source-to-browser slice. The
compiler-owned authored and resolved core is in `Copeland.TS.Mir.Machina`; the
bounded source profile is `Copeland.TS.MachinaSource.MachinaSourceCompiler`.
It parses ordinary `.ts`/`.tsx` Copeland source and lowers directly to the
same `MachinaView` tree used by the resolver and browser backend.

The core intentionally follows the CTS-MACHINA-INTENT-M1 correction: normal
Machina layout is resolved to explicit frames before browser lowering. CSS
flex and grid are not used as a second layout solver.

## Authored model

The authored tree is `MachinaView`. Its stable identities are derived during a
deterministic preorder walk (`root`, `root/0`, `root/0/1`), so a visual node
does not require an author-supplied id. Each node keeps an optional
`MachinaSourceSpan` for future source diagnostics and tooling.

The normalized core function surface is `Root`, `Container`, `VStack`,
`HStack`, `Text`, `Button`, `Toggle`, `Absolute`, `Anchor`, `Fixed`, and
`Fill`. In Copeland source the profile uses ordinary calls:

```ts
function SettingsPage(): View {
    return Root([
        VStack(
        [
            Text("Settings", { main: Fixed(40px), cross: Fill() }),
            Button("Save", SettingsEvent.Save, {
                main: Fixed(40px),
                cross: Fill(),
                style: PrimaryButton
            })
        ],
        {
            frame: Anchor({ left: 24px, right: 24px, top: 20px, bottom: 20px }),
            gap: 16px
        })
    ]);
}
```

User functions returning `View` compose as ordinary calls; no registration,
base class, id, parent/order row, or renderer slot is required. The initial
profile intentionally accepts static, parameterless View helpers. Parameter
binding from ordinary models/reducers is the next narrow extension.

`MachinaStyle` is an immutable record divided into `box`, `surface`, `text`,
`border`, and `effect` groups. Ordinary record derivation maps directly to its
semantics:

```csharp
var primary = buttonBase with {
    Surface = buttonBase.Surface! with { Fill = "#2563eb" }
};
```

The browser lowerer structural-hashes equivalent static style records and emits
one deterministic generated class. CSS is output, not a layout or style
authoring language.

## Geometry

`MachinaLength` is an affine value:

```text
resolved length = uiCoefficient * parentAxis + pxOffset
```

Source spelling is `120px`, `0.5ui`, and `-2px`. `ui` requires
`0 <= value <= 1`; unitless numbers are diagnosed in layout positions.
Addition and subtraction retain the affine form, so:

```text
0.25ui - 2px
```

resolves on a 400px parent axis to 98px. This supports mixed `ui` plus `px`
without CSS percentages or arbitrary symbolic expressions.

`MachinaAbsoluteFrame` resolves `x`, `width` against parent width and `y`,
`height` against parent height. `MachinaAnchorFrame` resolves exactly two
constraints on each axis: start/end/size. Negative resolved sizes are rejected.
Both support `ui` and mixed affine values.

## Deterministic layout and measurement

`VStack` and `HStack` use `MachinaStackOptions`. Fixed tracks are resolved,
gaps are removed, then remaining main-axis space is divided among `Fill`
tracks by explicit positive weight. The cross axis fills by default. Child
offsets are applied only after allocation, so `-2px` moves that child without
changing any sibling allocation.

Text can explicitly carry `TextWrap` as a measurement dependency. Its outer
frame remains resolved; browser text layout measures/wraps only inside that
frame and unrelated siblings remain resolved. Content-sized stack tracks are
currently rejected rather than silently making sibling placement browser
negotiated.

`MachinaResolvedDocument.ToDebugText()` emits deterministic resolved boxes,
node identities, source spans, measurement dependencies, and frame equations.
It is the current debug/fixture artifact.

## Browser lowering

`MachinaBrowserLowerer` consumes only resolved boxes. It emits semantic HTML:
`main`, `section`, `p`, `button`, and checkbox `input`, plus `data-machina-event`
for the typed-event binding seam. Each non-root frame receives:

```css
position: absolute;
left: ...px;
top: ...px;
width: ...px;
height: ...px;
box-sizing: border-box;
```

The root is the positioned containing frame. Neither generated frame CSS nor
the tested static style path emits `display: flex` or `display: grid`.

`MachinaBrowserPageBuilder` wraps that generated output in the small M1
browser host. It owns one state value and a pure `reduce(current, event)`
function. `Button` and `Toggle` event attributes carry static source event
symbols such as `SettingsEvent.Save`; the host dispatches them, obtains the
next state, and rerenders the affected visible status/control state. It has no
hook, store, middleware, component instance, virtual DOM, React, Vue, or
Blazor runtime.

TS-XML is optional alternate syntax. For example, `<VStack frame={...}
gap={16px}>` and its `<Text>`, `<Button>`, and `<Toggle>` children bind to the
same options and produce matching resolved geometry as the function form.
Whitespace-only child text is ignored. M1 deliberately defers generic tags,
spreads, lowercase host tags, and complex/multiple child-group mappings.

## Boundaries and follow-up

MachinaLayout.JS remains the semantic reference and fixture source; no npm
dependency is introduced. Machina.UI remains the downstream native reference:
the resolved rectangles, identities, and measurement markers are compatible
with a future `Rect`/presentation/hit-test lowering boundary.

The completed M1 slice deliberately defers:

- grid, profile variants, content-dependent stack reflow, and C# `styles.cs`
  ingestion;
- full general-language typechecker/backend support for arbitrary dynamic
  `View` expressions and parameterized View functions;
- full reducer-function lowering through the general JavaScript backend.

The browser proof source and materializer live in
`samples/copeland-ts/machina-m1`. It writes a static HTTP-ready `wwwroot`
artifact and `resolved.txt` debug artifact. Future features must build on this
MIR core rather than reintroduce CSS layout negotiation or a row-first
authoring surface.
