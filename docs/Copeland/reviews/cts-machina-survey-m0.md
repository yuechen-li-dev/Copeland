# CTS-MACHINA-SURVEY-M0 — Copeland-Native Layout and Style Design Survey

> Superseded for browser-layout policy by CTS-MACHINA-INTENT-M1. The canonical
> implementation reference is `docs/Copeland/reference/machina-layout.md`.
> In particular, the survey's recommendation to lower normal browser stacks
> through CSS flex/grid is no longer current: non-text Machina geometry is now
> pre-resolved and lowered as explicit absolute frames.

## 1. Executive recommendation

Adopt a compiler-owned Machina view contract in which ordinary Copeland
functions return `View`, optional TS-XML is syntax for the same typed calls,
and the compiler lowers the result to separate authored layout and style MIRs.
Do **not** adopt `LayoutRow[]` as the Copeland source language and do not take
MachinaLayout.JS as a runtime dependency for a Copeland browser application.

The recommended first implementation is **CTS-MACHINA-MIR-M1**: one closed
browser slice proving plain functions, TS-XML, immutable style records, and a
medium settings screen all lower to the same inspectable MIR. The browser must
lower stacks and grid structurally to semantic HTML and CSS flex/grid; it must
not resolve normal documents into positioned rectangles.

This recommendation preserves the useful semantic core of the current work:

```text
Copeland TS/TS-XML
  -> typed View and Style values
  -> Machina authored-layout MIR + style MIR
  -> browser HTML/CSS/event bindings

same MIRs
  -> optional resolved-layout MIR
  -> Machina.UI presentation preparation / Aurelian adapter later
```

## 2. Evidence inspected

| Area | Evidence inspected | Finding |
| --- | --- | --- |
| MachinaLayout.JS | Working copy at `C:\Users\yuech\source\repos\MachinaLayout.JS`, commit `0e142a1935333370b178928210b542d83c8dea54`; `src/types.ts`, `src/machina/*`, `src/style/*`, `src/dispatch/*`, React/Vue adapters, docs, and tests. | Its layout resolver, typed frame vocabulary, style grouping, CSS serializer, and deterministic tests are strong semantic prototypes. Its public authoring remains a compatibility layer over ids, rows, slot strings, and framework adapters. |
| Machina.UI / JTF | `Machina.Layout`, `Machina.Presentation`, `Machina.Runtime`, `Machina.Standard`, their tests, `Aurelian.Machina` integration tests, and the prior vendor review. | The resolved geometry, presentation-operation, input, hit-test, and screen-stack contracts are useful downstream targets. Current source authoring has overlapping `UiNode`, row, standard-helper, and flat-view paths and is not the Copeland authoring API to preserve. |
| Copeland TS | `Copeland.TS` syntax/binder/lowerer, `Copeland.TS.Mir`, JavaScript and C# backends, record tests, TS-XML tests, manifest binder, and browser host tests. | Records, nominal enums, exhaustive control flow, ordinary functions/modules, `with`, MIR, ESM emission, C# emission, and a source-spanned TS-XML parser already remove much JavaScript framework scaffolding. TS-XML currently parses but binding deliberately reports `COPE-TSXML-0101` outside a future semantic profile. |

The reference working copy has two untracked files (`docs/tspack-dvt-m44a.md`
and `manifest.tsx`); they were observed but not read or modified. This survey
changes only this report in Copeland.

## 3. Current state and what rows taught us

MachinaLayout.JS defines a good deterministic layout kernel: `LayoutRow` has
an id, parent, order, frame, optional stack/grid arrangement, view/slot,
layer, and responsive overrides. Its compiler rejects duplicate ids, unknown
parents, cycles, and invalid graph structure. Its resolver handles root,
anchor, fixed, fill, guide, cell, stack, and grid frames. `M.*` makes trees
more pleasant, but `M.root("app", ...)`, `M.fixed("header", ...)`, and every
escape hatch still ultimately lower to rows.

The row model is valuable *below* authoring. It is poor primary source for an
ordinary interface because hierarchy, node identity, child ordering, geometry,
render slot selection, and state data routing are split across distant records.
The existing counter sample illustrates this: a small screen needs seven rows,
manually coordinated parent/order/id fields, a `VIEWS` registry keyed by
strings, and separate `nodeData`/`viewData` maps before it reaches the React
renderer. `M.*` removes some parent injection but still requires pervasive
explicit ids and introduces positional overloads.

Rows remain excellent as a **debug/compatibility serialization**, fixture
shape, import format, table-editor format, resolved-layout input, and test
oracle. They should not be the compiler's only in-memory representation. A
tree-shaped authored MIR preserves source hierarchy and child order directly;
a deterministic preorder row projection can be supplied wherever the existing
resolver or a tooling workflow benefits from rows.

## 4. Proposed authoring philosophy and smallest `View` contract

`View` is a closed, compiler-recognized nominal union at the authoring
boundary. A user writes ordinary functions; built-ins are ordinary library
functions whose return type is `View`. The compiler recognizes the returned
value contract, not a special component base class or runtime registry.

```ts
export type View = /* compiler-known opaque view value */;

export function Toolbar(model: EditorModel): View {
    return HStack(
        [
            Button("Save", () => model.send(EditorEvent.Save)),
            Button("Cancel", () => model.send(EditorEvent.Cancel))
        ],
        { gap: 8, align: Align.Center }
    );
}
```

The smallest useful public contract is therefore `View`, `View[]`, typed
layout option records, typed style records, and typed event/action values.
There is no public `id`, `parent`, `order`, DOM tag, class name, or renderer
slot in the common case. `Text`, `HStack`, `VStack`, `Grid`, `Overlay`,
`Scroll`, `Button`, `Input`, and `Image` are regular typed functions. The
compiler only needs intrinsic recognition for the small set that creates a
Machina semantic node or event binding; `Card`, `Panel`, `Field`, and user
widgets expand through ordinary function calls.

Use children-first signatures for container widgets. They make dynamic lists
natural and prevent a large anonymous record from becoming the primary visual
shape:

```ts
VStack(children, { gap: Spacing.Large, style: Styles.Page });
Grid(children, { columns: [Track.Fixed(240), Track.Fill()], gap: 16 });
Button("Save", onSave, { style: Styles.PrimaryButton });
```

This shape has one predictable positional convention: semantic required data,
then optional options. Components with several required semantic values may
instead take a named props record. The compiler should diagnose a non-`View`
child at the original child expression and an unknown option at the option
property.

### Identity, conditionals, repetition, state, and dispatch

The compiler assigns a deterministic path-derived identity from module,
function, source span, and static child ordinal. Authors supply a `key` only
for a repeated collection whose members require preservation across reorder;
the resulting identity appends that stable key. A manual `id` is reserved for
host addressing, restoration, test anchors, or cross-node constraints—not
required on every visual node.

Conditionals are ordinary expressions returning `View` or an explicit
`View.None`; repetition is an ordinary `View[]` expression. `View.Children`
normalizes both before MIR construction. Component state stays in the
application's typed model (or a later explicit state contract), never hidden
in a component instance registry. Browser events lower to typed action
descriptors that call the ordinary reducer/dispatch endpoint and trigger the
normal rerender law.

## 5. Representative authoring comparison

### A. Small counter

Existing MachinaLayout.JS uses seven named rows plus React slot registry and
string event `"counter.increment"`. The proposed source is one tree and one
typed event:

```ts
enum CounterEvent { Increment }
record CounterModel { count: number; }

export function Counter(model: CounterModel): View {
    return Card(
        VStack(
            [
                Text("Counter", { style: Styles.Heading }),
                Text(`Count: ${model.count}`, { style: Styles.Count }),
                Button("Increment", () => send(CounterEvent.Increment))
            ],
            { gap: 12, align: Align.Center }
        ),
        { style: Styles.CounterCard }
    );
}
```

### B. Medium settings screen — function and TS-XML equivalence

Plain functions are the recommended default:

```ts
export function SettingRow(
    label: string,
    control: View
): View {
    return HStack(
        [Text(label, { fill: true }), control],
        { align: Align.Center, gap: 12 }
    );
}

export function SettingsPage(model: SettingsModel): View {
    return VStack(
        [
            Text("Settings", { style: Styles.Heading }),
            Card(
                VStack(
                    [
                        SettingRow(
                            "Dark mode",
                            Toggle(model.darkMode, () => model.send(SettingsEvent.ToggleDarkMode))
                        ),
                        SettingRow(
                            "Email alerts",
                            Toggle(model.emailAlerts, () => model.send(SettingsEvent.ToggleEmailAlerts))
                        )
                    ],
                    { gap: 12 }
                ),
                { style: Styles.Card }
            ),
            Button("Save", () => model.send(SettingsEvent.Save), {
                style: Styles.PrimaryButton
            })
        ],
        { gap: 16, style: Styles.Page }
    );
}
```

The equivalent optional TS-XML makes hierarchy salient without introducing
different component semantics:

```tsx
export function SettingsPage(model: SettingsModel): View {
    return (
        <VStack gap={16} style={Styles.Page}>
            <Text style={Styles.Heading}>Settings</Text>
            <Card style={Styles.Card}>
                <VStack gap={12}>
                    <SettingRow label="Dark mode">
                        <Toggle value={model.darkMode}
                                onChange={() => model.send(SettingsEvent.ToggleDarkMode)} />
                    </SettingRow>
                    <SettingRow label="Email alerts">
                        <Toggle value={model.emailAlerts}
                                onChange={() => model.send(SettingsEvent.ToggleEmailAlerts)} />
                    </SettingRow>
                </VStack>
            </Card>
            <Button style={Styles.PrimaryButton}
                    onClick={() => model.send(SettingsEvent.Save)}>Save</Button>
        </VStack>
    );
}
```

### C. Larger control-room slice

The existing `control-room` sample resolves a separate `buildDemoRows(...)`
result, maintains a view registry and data maps, and passes a resolved document
to `MachinaReactView`. Its useful semantics are a responsive shell, an
inspector, a preview, and a floating action; its rows are not the desired
surface. A Copeland slice is instead:

```ts
export function ControlRoom(model: ControlRoomModel): View {
    return Grid(
        [
            Cell(Inspector(model), { column: 0, row: 0 }),
            Cell(Preview(model), { column: 1, row: 0 }),
            Overlay(Button("Reset", () => model.send(ControlRoomEvent.Reset)), {
                horizontal: Align.End,
                vertical: Align.End
            })
        ],
        {
            columns: [Track.Fixed(320), Track.Fill()],
            rows: [Track.Fill()],
            gap: 16,
            style: Styles.ControlRoom
        }
    );
}
```

This retains explicit grid and overlay intent but removes manual rows,
coordinate calculation, slot strings, and registration bookkeeping. It is
also a realistic future conformance fixture, not a line-by-line translation.

| Form | Strength | Cost | Decision |
| --- | --- | --- | --- |
| Existing rows | Stable serialization, easy table editing, resolved geometry. | Parent/id/order/slot bookkeeping; weak hierarchy and composition. | MIR/debug/compatibility only. |
| `M.*` tree helper | Improves parent injection and stack/grid readability. | Still requires ids and retains row geometry/adapter worldview. | Semantic reference and compatibility authoring only. |
| Plain Copeland functions | Best for shallow trees, loops, helpers, typed calls, diffs, and LLM generation. | Nesting can become punctuation-heavy at exceptional depth. | Default. |
| TS-XML | Best for visual hierarchy and mixed text/children. | More verbose for computed lists and small widgets. | Optional alternate syntax. |

## 6. TS-XML semantic law

TS-XML is alternate syntax for a typed call; it has no runtime and no
TS-XML-only component model. The existing parser already retains exact source
spans for element boundaries, attributes, text, expressions, nesting, and
fragments. M1 should add a view semantic profile rather than change parsing.

```text
<VStack gap={16} style={Styles.Page}> ...children... </VStack>
  ==>
VStack([ ...lowered children... ], { gap: 16, style: Styles.Page })
```

- Uppercase element names resolve as an ordinary value/function in lexical
  module scope. Lowercase names are reserved for a later typed host-element
  profile; M1 need not implement them.
- Attributes bind to named parameters/options by the function's view signature.
  Text attributes and braced expressions retain their parsed source spans.
- A text child lowers to `Text(string)` only for a child position whose
  signature accepts `View`; whitespace-only formatting text is ignored.
- Element and braced-expression children lower in source order. A fragment
  lowers to a child list. Optional child expressions normalize through
  `View.Children`; no hidden null semantics are needed.
- A function may declare one children parameter. Multiple child groups require
  explicit named view-valued properties/functions, avoiding an implicit JSX
  convention. Generic components are ordinary generic calls after type
  inference; M1 may defer explicit type arguments in tag syntax.
- TS-XML diagnostics attach to the tag name, attribute token, or child span.
  The parser's current `COPE-TSXML-*` diagnostics remain authoritative for
  malformed syntax; the new profile owns resolution and type diagnostics.

## 7. Proposed authored-layout MIR

The authored-layout MIR is a typed tree after component expansion and before
backend selection. It carries intent, never browser classes or absolute
browser rectangles.

```ts
type MachinaViewDocument = {
    root: MachinaViewNode;
    source: SourceSpan;
};

type MachinaViewNode = {
    id: ViewNodeId;
    kind: ViewKind;
    children: readonly MachinaViewNode[];
    layout: LayoutSpec;
    style: StyleBinding;
    content?: ViewContent;
    events: readonly ViewEventBinding[];
    semantics?: SemanticRole;
    source: SourceSpan;
};

type LayoutSpec =
    | { kind: "flow" }
    | { kind: "stack"; axis: "horizontal" | "vertical"; gap: Length; align: Align; justify: Justify; padding: Insets; size: SizeSpec }
    | { kind: "grid"; columns: readonly Track[]; rows: readonly Track[]; gap: GridGap; padding: Insets; size: SizeSpec }
    | { kind: "overlay"; horizontal: Align; vertical: Align; size: SizeSpec }
    | { kind: "scroll"; axis: "horizontal" | "vertical" | "both"; size: SizeSpec }
    | { kind: "anchor"; edges: AnchorEdges; size: SizeSpec };

type SizeSpec = { width?: "fill" | Length; height?: "fill" | Length };
type StyleBinding = { static?: StyleId; dynamic?: DynamicStylePlan };
type ViewEventBinding = { event: ViewEvent; action: TypedAction; source: SourceSpan };
```

`ViewKind` initially includes `Text`, `Button`, `Input`, `Image`, `Container`,
and `Host`. `VStack`/`HStack`/`Grid`/`Overlay`/`Scroll` are layout nodes,
while `Card` expands to a `Container` with its own style. `Host` is the narrow
escape hatch for a typed browser/native host contract or future platform
integration. It must carry a declared contract, not arbitrary DOM/CSS text.

Static conditionals and repetitions are expanded before this document. Dynamic
ones lower into `Conditional` and `Repeat` child-plan nodes with a stable
anchor id and per-item key. Responsive variants are represented as named,
ordered mode layers on `LayoutSpec` (`wide`, `compact`, `narrow`) but are
deferred in M1. Grid and overlay are represented now but can be deferred from
the first browser implementation. A deterministic debug serialization emits
preorder records with path ids and source spans; a compatibility row projection
can add parent, order, and resolved-frame information without being the MIR.

Padding belongs to layout **when it defines the content box used for child
arrangement**. It belongs to style only when it is intrinsic component chrome.
M1 should allow layout padding on containers and style padding on leaf controls
but diagnose or reject ambiguous double ownership on the same intrinsic
container. Gap, direction, tracks, fill/fixed sizing, alignment, anchors, and
scroll extent are layout. Color, radius, type, border, opacity, and shadows
are style. Overflow is a layout policy whose browser lowering uses CSS
overflow; clipping decoration remains style/presentation detail.

## 8. Proposed style MIR and authoring law

Styles are ordinary immutable typed records. Copeland's existing record
identity and `with` MIR already give the JavaScript `S.with` use case without
a helper. Nested replacement is explicit and is evaluated in normal language
order:

```ts
export const ButtonBase: Style = {
    surface: { fill: Colors.Slate900, radius: 8 },
    text: { color: Colors.White, weight: FontWeight.Semibold },
    box: { paddingX: 16, paddingY: 10 },
    border: { width: 1, color: Colors.Slate700 }
};

export const PrimaryButton: Style = ButtonBase with {
    surface: ButtonBase.surface with { fill: Colors.Blue600 }
};
```

The compiler recognizes `Style` and token contracts, not `styles.ts` magic.
`styles.ts` is the ordinary recommended Copeland module convention. A
`styles.cs` file may define matching C# records and `with` expressions where a
C# host owns styles. Both inputs must normalize to the same MIR, but C# source
support is a later interop proof, not a reason to embed C# in the M1 browser
slice.

```ts
type MachinaStyleSheet = {
    tokens: readonly StyleToken[];
    styles: readonly StyleDefinition[];
};

type StyleDefinition = {
    id: StyleId;
    value: StyleValue;
    variants: readonly StyleVariant[];
    source: SourceSpan;
};

type StyleValue = {
    box?: { padding?: Insets; minSize?: SizeBounds };
    surface?: { fill?: Color; radius?: Length; opacity?: number };
    text?: { color?: Color; font?: FontRef; size?: Length; lineHeight?: number; weight?: FontWeight; align?: TextAlign };
    border?: { color?: Color; width?: Length; style?: BorderStyle };
    effect?: { shadow?: Shadow };
};

type StyleVariant = {
    when: StylePredicate; // named state or named responsive mode
    patch: StyleValue;
    source: SourceSpan;
};
```

Tokens have typed categories (`Color`, `Length`, `Font`, `Shadow`), names, and
values; style references are symbolic until lowering. A record literal/`with`
chain that depends only on compile-time values is static, structurally hashed
after canonical field order, deduplicated, and assigned a stable `StyleId`.
It becomes one deterministic CSS class. A style expression that depends on the
model is a `DynamicStylePlan`: it may only vary whitelisted value slots and
lowers to stable CSS custom properties on the node. It must never cause a
runtime CSS-string or selector generator.

State and responsive variants are explicit patches applied in fixed order:
base -> responsive mode -> state. The MachinaLayout.JS `S.set`, `S.inherit`,
`S.unset`, `S.over`, and `S.compose` were necessary to emulate immutable
composition and removal in JavaScript. Copeland replaces ordinary derivation
with records and `with`; retain an explicit `Style.Remove`/reset facility only
if a real backend-neutral removal case is demonstrated. Do not import
`inherit` as an accidental CSS-inheritance feature.

## 9. Backend lowering

### Browser

The browser backend emits semantic elements selected by `ViewKind` (`button`,
`input`, headings, labels, `main`, `section`, and so on) plus generated stable
classes. `VStack` becomes a flex column, `HStack` a flex row, `Grid` CSS Grid,
and `Scroll` normal-flow overflow. A structural node receives a generated
class for layout declarations and a deduplicated class for static style. Token
definitions become deterministic CSS custom properties. State variants lower
to data attributes owned by the view runtime, and responsive modes lower to
compiler-owned media queries. Native browser events call generated typed
action bindings; there is no React dependency, selector cascade, source-order
cascade, `!important`, or CSS-in-JS runtime.

```text
VStack(children, { gap: 16, style: Styles.Page })
  -> <main class="m-layout-... m-style-...">...</main>
  -> .m-layout-... { display:flex; flex-direction:column; gap:16px; }
  -> .m-style-... { background:...; color:...; }
```

Explicit geometry resolution is reserved for anchor/guide-like layout,
screenshots, hit testing, exact canvas/native rendering, and future
constraints that cannot lower faithfully to normal flow. It is not the browser
default.

### Native / Machina.UI

The native backend consumes the same authored tree, lowers `LayoutSpec` to a
Machina.UI-compatible layout document, then resolves it against an explicit
viewport only when geometry is required. `LayoutDocument`,
`ResolvedLayoutDocument`, `Rect`, `StackArrange`, `GridArrange`, frames, and
the presentation frame/operations are useful downstream reference contracts.
`MachinaPresentationFrame` is especially valuable as immutable
backend-neutral prepared presentation intent. The Aurelian adapter should
consume prepared geometry/presentation commands through an Aurelian-owned
boundary; it must not make Aurelian core depend on Machina UI authoring or a
desktop host.

## 10. Existing implementation disposition

| Subsystem / current machinery | Disposition | Reason |
| --- | --- | --- |
| MachinaLayout.JS frame vocabulary, stack/grid/fill/fixed/anchor semantics | Lower into compiler MIR | Strong cross-target semantic prototype. |
| `LayoutRow[]`, parent/order/id validation, row serializer | Keep as compatibility/debug serialization | Good interchange and resolver/test form; bad primary source. |
| `M.*` helpers | Retain as reference/compatibility only | Its tree ergonomics teaches useful shapes but keeps ids and row lowering. |
| React/Vue/React Native view adapters and registries | Retire from Copeland's core path | Browser backend owns generated HTML/events; adapters remain reference integrations. |
| MachinaStyle groups, tokens, deterministic CSS serialization | Lower into style MIR | The grouped vocabulary and static artifact discipline are useful. |
| `S.with`, `S.merge` | Replace with records and `with` | Copeland already has typed immutable replacement. |
| `S.compose`, `S.over`, set/inherit/unset slots | Defer narrowly | Keep only if a concrete cross-target patch/removal law proves necessary. |
| Style rule/token tables | Compatibility/tooling input | Useful for imports/editor tooling; not the primary typed source language. |
| JavaScript string dispatch tables | Replace with typed enums/actions and reducers | Copeland enum/call/record features make names and state transitions statically checkable. |
| Machina.UI `LayoutDocument` / resolved geometry | Keep as downstream/native reference | Clean renderer-neutral geometry contract. |
| Machina.UI presentation operations, input batch router, hit test concepts, screen stack | Keep as native backend contracts/reference | Better native semantics than the JavaScript adapters. |
| Machina.UI standard widgets/theme helpers | Compatibility/reference | Useful fixtures, but overlapping experimental authoring paths must not freeze the Copeland API. |
| Current row-first and flat/hosted experiments | Retire as future public authoring candidates | They are historical/compatibility paths, not the recommended source model. |

## 11. Testing and conformance strategy

Make the compiler's canonical debug serializations the primary test artifact:

1. Function syntax and TS-XML syntax for each fixture produce byte-identical
   authored layout MIR and style MIR after source-span fields are ignored.
2. Validate static style canonicalization, `with` derivation, token typing,
   class identity, and dynamic CSS-variable plans independently.
3. Test browser structural lowering as HTML/CSS/event artifacts, not visual
   pixel positions, for stack/grid/scroll/common semantic widgets.
4. Test resolved-layout/native lowering only for concepts that require
   geometry; compare expected rectangles, presentation operations, and hit
   testing where meaningful.
5. Add deterministic source diagnostics for unknown widget/options, invalid
   children, bad TS-XML attributes, dynamic static-style violations, duplicate
   repetition keys, and unsupported host contracts.

Translate MachinaLayout.JS `machina/stack`, `grid-*`, `anchor`,
`responsiveVariants`, `machina-style*`, and dispatch tests into focused
Copeland fixtures. Treat the React/Vue tests as adapter-semantic reference
only. Reuse the current Machina.UI stack/grid/layout compiler/resolver and
presentation tests as native/reference fixtures; do not aggregate every
historical fixture in one new project.

## 12. Risks and unresolved questions

- **Padding ownership:** M1 must settle the container-content-box versus
  intrinsic-control distinction with a few fixtures before exposing both.
- **Dynamic styles:** Restrict M1 to static records; CSS-variable slots need a
  typed, observable update plan and should not become arbitrary style strings.
- **Responsive policy:** named modes are preferable to arbitrary author media
  queries, but the exact mode names/breakpoints and interaction with container
  size remain open.
- **TS-XML surface:** parser support exists, but generic tag syntax, lowercase
  typed host elements, and multiple child groups should remain deferred until
  the function-call law is proven.
- **Identity:** source-path identities work for static trees; repeated keyed
  identity and state restoration need an explicit future contract.
- **Native parity:** normal browser flow and exact native geometry will not
  have identical capabilities. The conformance boundary must identify the
  subset expected to match rather than force the browser into rectangles.
- **Input/accessibility:** Machina.UI has lifecycle/pointer-oriented work; a
  browser-native semantics, focus, keyboard, input, and accessibility contract
  needs dedicated work beyond M1.

## 13. Exact next milestone: CTS-MACHINA-MIR-M1

Implement only this closed proof:

```text
ordinary Copeland functions + optional TS-XML
  + static typed Style records with `with`
  -> identical Machina authored layout/style MIR
  -> deterministic semantic HTML + generated CSS
  -> a browser-rendered SettingsPage with Button and Toggle events
```

Scope:

- `View`, `Text`, `VStack`, `HStack`, `Card`, `Button`, and `Toggle`;
- static tokens/styles and nested `with` derivation;
- one typed action/event path and rerender proof;
- TS-XML binding only for the equivalent small intrinsic set;
- source-spanned MIR/debug text and focused fixtures.

Out of scope: grid, overlay, scroll, arbitrary host elements, responsive
variants, dynamic styles, C# style-source ingestion, React/Vue compatibility,
SSR/hydration, native renderer work, routing, and a migration of existing
Machina authoring.

### M1 acceptance criteria

1. A function-authored and TS-XML-authored settings screen produce identical
   layout/style MIR excluding source spans.
2. The browser emits semantic HTML and deterministic generated CSS; the stack
   path uses flex rather than absolute rectangles.
3. `Style` record values and nested `with` expressions type-check, lower,
   canonicalize, and yield stable class names across rebuilds.
4. The counter action is a typed event/action, dispatches through the real
   browser path, rerenders, and updates visible text.
5. A user-defined `SettingRow` ordinary function composes without registration
   or inheritance.
6. Diagnostics identify an invalid TS-XML property and invalid `View` child at
   their precise source spans.
7. MIR serialization has source spans and deterministic path identities;
   no visual node requires a handwritten id.
8. Focused compiler/backend tests cover function/TS-XML equivalence, styles,
   event lowering, and browser artifacts.
9. No production dependency on MachinaLayout.JS, React, or Vue is added.
10. No native geometry resolution is used on the normal browser stack path.

## 14. Additional work performed

- **Change:** added this documentation-only survey.
- **Reason:** record grounded design decisions before any broad UI
  implementation.
- **Semantic impact:** none on compiler, runtime, browser, or native behavior.
- **Validation:** documented below; no reference checkout files were modified.
- **Follow-up:** begin only `CTS-MACHINA-MIR-M1` after this recommendation is
  accepted.

## 15. Validation

Required repository validation for this documentation-only change:

```text
dotnet build Copeland.slnx --no-restore
dotnet test Copeland.slnx --no-build
git diff --check
```

Results (2026-07-26):

- `dotnet build Copeland.slnx --no-restore` passed with 0 warnings and 0
  errors using .NET SDK 10.0.302.
- `dotnet test Copeland.slnx --no-build` ran the full solution test set. Five
  existing corpus-artifact stability assertions failed, while the remaining
  executed tests passed: `Nominal_union_corpus_artifacts_have_stable_bytes_and_hashes`
  (expected 1268 bytes, actual 1320),
  `Callable_reference_corpus_is_byte_stable_in_all_emission_profiles`
  (expected 1480 bytes, actual 1518), and the C# corpus hash checks for
  `Table`, `Inferred_reuse`, and `Pure_class`. These assertions are in
  `Copeland.TS.Tests.NominalUnionTests`, `Copeland.TS.Backend.CSharp.Tests.CallableCorpusTests`,
  and `Copeland.TS.Backend.CSharp.Tests.CSharpCorpusTests`; a docs-only report
  cannot change their generated artifact bytes or hashes. No corpus baseline
  was changed.
- `git diff --check` passed.
