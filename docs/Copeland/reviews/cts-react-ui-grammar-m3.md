# CTS-REACT-UI-GRAMMAR-M3

Status: success. Implemented and qualified through the real compiler/browser path.

This implements the contribution-stream direction and named capture expressions
from the [M2 audit](cts-react-capture-stream-audit-m2.md). The current React
friction pages exercise the new grammar through TSPack and the real browser.

## Accepted source law

An originless UI stream constructs an immutable value of named, singular
`ReactNode` contributions. A layout remains the owner of arrangement and origin.
`bind Layout using Content` is an ordinary `ReactNode` expression.

```ts
layout Wide<0px, 0px> {
    width: fill;
    height: fit;
    row root {
        slot list { width: 300px; height: fit; }
        slot detail { width: fill; height: fit; }
    }
}

layout Compact<0px, 0px> {
    width: fill;
    height: fit;
    column root {
        slot list { height: fit; }
        slot detail { height: fit; }
    }
}

function IssueBody(state: AppState, send: (event: AppEvent) => void): ReactNode {
    stream Content {
        list: IssueList(state, send);
        detail: IssueDetail(state);
    }
    if (state.narrow) {
        return bind Compact using Content;
    }
    return bind Wide using Content;
}
```

Content expressions run once in authored order when the declaration executes.
Binding reads the constructed values; it does not reevaluate their expressions.
Layout traversal determines presentation order independently of construction
order. A subsequent function invocation constructs fresh local content.

Each layout is independently validated. Existing `layout type` topology
contracts remain exact. Expression binding also checks the exact channel set:
missing, extra/non-slot, duplicate, and non-renderable contributions diagnose.
A concrete layout without an explicit `satisfies` contract receives an internal
shape from its already-bound topology; this does not make row and column
contracts interchangeable. Slot names remain unambiguous under the existing
layout binder's declaration rules.

Container spacing/style belongs on the container that owns it. For example,
the article's `column root` owns its 12px gap and 16px padding. Header dimensions
are carried into the root by the shared layout projection. Fixed roots use the
existing resolved Machina projection, including origin offsets. Fit/fill roots
use the existing browser flow projection and preserve their explicit origin.

## Declaration placement and timing

| Placement | Construction | Visibility |
| --- | --- | --- |
| Module `stream Content { ... }` | Once when the module initializes | Ordinary module declaration; `export` and named import aliases supported |
| Class `stream Content { ... }` | Once with its defining module | Associated immutable data, accessed as `Class.Content`; public/private class checks apply |
| Local `stream Content { ... }` | Each time execution reaches the statement | Ordinary lexical scope; usable inside normal functions and associated class function bodies |

Class streams introduce no receiver or per-instance field. Existing pure class
construction rules still apply. The optional `static` modifier makes no change
to their associated-data meaning.

Module/class initialization follows declaration order within an origin module;
project modules follow the existing dependency order. Known transitive reads of
a not-yet-initialized stream diagnose with `COPE-STREAM-CONTENT-0007`, at the
authored stream declaration. An indirect invocation with unknown initialization
dependencies is also rejected on this eager module path; the repair is local
construction or a direct named invocation. This is not a lazy initialization
cache. A module stream is not refreshed by a later render.

The current article declares module-level `ArticleChrome` for stable heading
and notes, and local `ArticleContent` for its changing body. Issue triage has
one local `IssueContent` shared by its wide and stacked arrangements. Settings
and the intentional overflow specimen retain their original origin-bearing
inline streams, providing compatibility coverage in the same application.

## Named capture expressions

```ts
const search = capture {
    send,
    query: state.queryDraft
} () => send(AppEvent.Search(query));
```

Shorthand names retain their existing meaning. Named initializers resolve in
the surrounding scope, evaluate once from left to right, and create immutable
environment slots. An earlier capture does not supply the scope of a later
initializer. Duplicate names, parameter collisions, uncaptured outer reads,
and capture reassignment retain their existing checks and limits.

This extends the existing callable construction and ordered environment values.
It changes neither public callable signatures nor the underlying value policy.
Reference-like/host values are not recursively copied. Old callbacks observe
their original snapshot; capturing `send` still delivers an event to the state
owner, which reduces its current state. Search and Save now capture only the
draft string they need rather than the entire application state.

## Implementation ownership

- Parser nodes distinguish contribution declarations/binding expressions from
  geometry-bearing UI streams and shader streams. Existing syntax-kind values
  are preserved; new kinds are appended. The missing-origin repair remains
  covered by the existing geometry-stream LSP specimen.
- The binder represents contributions through ordinary immutable record
  construction. It checks channels against the concrete layout and retains
  authored component expressions separately from their runtime field reads.
- `BoundLayoutBinding`, existing neutral hosts, and the Machina normalizer and
  browser emitter remain the spatial path. There is no second layout engine.
- Ordered `MirModuleValue` facts own eager storage, with ordinary typed MIR
  initializer functions. Shared validation checks identities, initializer
  signatures, initialization reads, and immutable storage before emission.
  C# materializes readonly fields; native ESM emits initialization only in the
  origin module. Imported UI bundles use the existing shared value-provenance
  runtime. A real multi-file Node probe qualifies this boundary without a
  browser-host dependency supplying it incidentally.
- LSP declaration/lexical completion and module hover recognize contributions.
  The protocol regression checks both the old origin repair and new module/local
  contribution source in a manifest-owned React project.

## Qualification

`dotnet build Copeland.slnx -m:1 -v:minimal` passes with zero warnings/errors.
The full closeout `dotnet test Copeland.slnx --no-build -m:1` run passes
**2,157 tests**, with zero failed or skipped. The initial full run caught the
geometry-stream missing-origin regression; its failed TRX is retained separately
from the repaired closeout. Tests are not limited to the new feature filters.

The focused runtime tests exercise all three JavaScript emission profiles,
eager local construction order, repeated binding without reconstruction,
module/class initialization, import aliasing, class privacy, retained topology
checks, and fixed-layout origin/dimensions. Named captures execute through C#
and JavaScript, including ordered mutation of the source binding, outer-scope
alias resolution, repeated callback invocation, and escaping snapshots.
Malformed module-value MIR is rejected by both backends. The UI itself remains
a JavaScript/React qualification; C# typed storage tests are not a C# UI claim.

The one-command friction run passes:

- strict baseline TypeScript and TypeScript tooling checks;
- both current TSPack builds and all 18 compiler-probe expectations;
- 74 paired browser observations with matching copy and no console/page/request
  diagnostics;
- 12px article gap, 16px padding, 300px wide sidebar, mobile arrangement, and
  selection retained through live resize and manual arrangement switches;
- controlled inputs, validation, loading/error/retry, real Base UI dialog focus,
  Escape, reopening, long copy, 200% root text size, and fallback font checks;
- original website regression: all 12 paragraphs remain reachable, including
  keyboard scrolling on bounded tablet/mobile cards.

The 180px visible-overflow specimen still deliberately overlaps in both
implementations. Matching channel names alone do not certify retention of
arbitrary React component-local state across topology changes. The proven
selection belongs to the explicit application state owner. Dynamic collections,
optional channels, generalized React state/effects delivery, SSR/hydration, and
additional browsers remain outside this increment.

Source accounting records one list and one detail binding site, down from two
each. Application lines increase from M1's 260 to 275 versus the baseline's
175: separately authored geometry and module data cost lines. This qualifies
content reuse and explicit timing, not universal brevity or a timed usability
advantage. The browser is Microsoft Edge 153.0.4234.48 using Playwright 1.56.1.

Tracked evidence:

- [Compiler probes](evidence/react-ui-grammar-m3/compiler-report.json)
- [Paired browser observations](evidence/react-ui-grammar-m3/browser-report.json)
- [Original website regression](evidence/react-ui-grammar-m3/website-regression.json)
- [Source accounting](evidence/react-ui-grammar-m3/authoring-accounting.json)
- [Full solution validation](evidence/react-ui-grammar-m3/validation.json)

Reproduce from the repository root:

```powershell
./samples/copeland-ts/react-friction-m0/run.ps1 -Grammar -FullValidation
```

The default run now selects grammar evidence. M0/M1 reports are retained as
historical results; ignored M3 screenshots live under
`samples/copeland-ts/react-friction-m0/artifacts/react-ui-grammar-m3/screenshots`.
