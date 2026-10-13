# CTS-PRESENTATION-FRICTION-M9: Copeland owns presentation

Status: **Success for the bounded shared-source milestone.** One unchanged
Copeland application/projection source set compiles and runs against React and
Vue. The issue list/detail surface now uses compiler-bound Machina style and
layout rules. This establishes the source/adapter boundary for a later mixed
framework host milestone.

## Source and ownership

`View` is a new, distinct compiler-owned type. It is neither a type alias nor
an implicit union of `ReactNode` and `VueNode`. Raw framework node values cannot
be assigned to `View`. Package component contracts remain the explicit gateway
for typed props/events and supported children.

The shared entry point has no framework import:

```typescript
export function Application(width: int): View {
    state: AppState = InitialState(width);
    on Apply(event: AppEvent) => Reduce(state, event);
    return presentViewport(IssuesPage(state, on.Apply),
        capture { send: on.Apply } (width: int) => send(AppEvent.Resize(width)));
}
```

`IssuesPage.ts` keeps its semantic records, matches, contribution stream, and
layout binding. `UI.tsx` is the optional document/control projection, shared by
both targets. It uses portable `class`/`for` spellings and the same typed
`SearchField`, `Panel`, and `ArrangementToggle` contract. Physical adapters
translate that contract to genuine Base UI React components or Vue SFC/Reka
components. Framework-specific prop names remain in those adapters.

Application state, transitions, effect phases, completion matching, cancellation,
and child retirement still use the existing canonical browser frame registry.
The projection registry for `View` is renderer-neutral. The framework owns its
rendered values and internal control interaction; it does not replace the
Copeland transition/effect engine.

## Adapter contract

`CopelandViews` selects this authoring surface. `PresentationAdapter` is an
explicit target contract carried from compiler options/resolved project context
through bound programs, module aggregation, and MIR. It participates in context
fingerprints and survives executable-artifact copies.

The contract contains data, not executable snippets:

| Field | Responsibility |
| --- | --- |
| `Identity` | Open adapter identity, matched against component contracts |
| `ElementModule` / `ElementExport` | Declared materialized JavaScript factory |
| `Children` | Variadic arguments, arrays, or a function-valued default slot |
| `ClassProperty` / `LabelForProperty` | Target prop conventions for Copeland intrinsic presentation |
| `CommitProtocol` | Qualified lifetime/subscription/commit acknowledgement behavior |

React and Vue are presets of this contract. They are not an exhaustive enum of
frameworks. A focused compiler test proves an arbitrary stateless adapter
identity and array-based factory. Stateful use of an adapter without a qualified
commit protocol is rejected with `COPE-VIEW-0004`; the compiler never assumes
that a factory call also provides safe effect acknowledgement.

The compiler imports the factory itself. Imported factory bindings are reused
across module dependencies rather than emitted twice. Required adapter/project
and materialization failures have exact `COPE-VIEW-0001` through `0004`
diagnostics. Framework-owned legacy project types remain available and distinct.

The React ownership visitor also preserves the compiler's fixed sibling
argument shape. Its metadata lives in a weak side table, not DOM attributes or
invented positional keys. A single dynamic child array retains its collection
shape. This fixes React warnings introduced by cloning fixed siblings into an
array while retaining authored keys for dynamic collections.

## Style and layout

The audit found that `MachinaStyle` already carried typography, but layout
authoring exposed only fill/border. `layout.style` now exposes radius and typed
text color, px size, integer weight, positive finite line-height ratio, and
checked `normal`/`anywhere` wrapping. These normalize into the existing Machina
style MIR and browser lowerer. Legacy style hashes remain unchanged when no
wrapping policy is authored.

The page declares 24 px padding on its owned list/detail containers, 12 px
spacing, 18 px text with a 1.6 line-height ratio, `anywhere` wrapping, 8 px
corners, and its surface/border colors. Layout templates select horizontal or
vertical topology; `with` applies those immutable configurations. The wide list
host retains its assigned 300 px width, including its own padding.

The list/detail projection no longer uses the shared `.panel` surface class.
Browser checks read the generated host rules and verify actual typography,
wrapping, decoration, padding, and layout dimensions. Shared shell/control CSS
remains. This is not a complete CSS replacement or native typography
qualification.

## Verification

- Full .NET solution build: **zero warnings/errors**. **2,242 tests passed**,
  zero failed/skipped. Sixteen new backend cases cover distinct types, all
  emission profiles, styles, child shape, open stateless adapter identity, and
  exact unsupported cases. One new context test checks descriptor round-trip
  and adapter-sensitive fingerprints.
- **192 mounted browser observations**, with no warning/error/page/network
  diagnostics. React and Vue run the same sources under Diagnostic, Symbolic,
  and Production at 1280, 768, and 390 px, alongside the idiomatic Vue baseline.
  Checks cover domain parity, search, selection, insertion/removal/reversal,
  status matching, controlled framework components, viewport changes, long
  text, actual doubled text sizes, absence of overflow, and Copeland surface
  styles.
- Eight lifecycle observations per target/profile qualify real HTTP typed
  completion, presentation-committed focus, stale result rejection, keyed
  reorder preservation, fresh state after reinsertion, typed failure handling,
  retired-owner rejection, and subtree teardown. React uses StrictMode.
- Legacy React regression: **55 compiler expectations**, seven state/on and
  twelve effects/children cases per profile, 74 paired page observations, and
  four semantic library assertions passed.
- Frozen Zig suites are unchanged and were not run.

`evidence/presentation-friction-m9/` contains source/emission hashes, browser
reports, screenshots, authoring counts, validation hashes, and separate React
regression reports. Full solution logs are under
`artifacts/validation/presentation-friction-m9-{build,tests}.log`.
Reproduction is in `samples/copeland-ts/presentation-friction-m9/README.md`.

## Generalization and next host boundary

This milestone selects one adapter per target. Simultaneous React/Vue islands,
other actual framework adapters, and live adapter switching remain unqualified.
The fixture calls the real compiler/project/backend API; it does not add a
general Vue/React TSPack target or framework source compiler.

The next milestone should qualify a Copeland-owned host containing a React
island and a Vue island together. Additional Svelte, Lit, Preact, Solid, or
managed Blazor bridges should implement the same ownership boundary:

1. Exchange contracted data props and typed event endpoints, keeping renderer
   node values private to the adapter.
2. Mount/update/unmount inside an assigned Copeland host, using existing
   attachment capabilities and lifetime policy. Updates preserve the existing
   island root; removal releases it exactly once.
3. Acknowledge the exact presented revision and owner generation. An asynchronous
   managed/remote bridge must discard stale acknowledgements and late callbacks.
4. Declare supported child/slot, text-measurement, and style reachability
   capabilities. Shadow DOM and foreign internal layout do not become
   Copeland-owned merely because their container has Copeland styling.

The first mixed-island proof should retain shared `state`/`on` ownership while
updating, reordering, removing, and reinserting foreign controls. Named/scoped
slots, SSR/hydration, HMR, Teleport/KeepAlive/Suspense, arbitrary framework
coverage, and `AttachmentsSettled` stay outside the current qualification.
The sample HTTP service remains bounded demonstration behavior.
