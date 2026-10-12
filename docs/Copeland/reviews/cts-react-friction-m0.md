# CTS-REACT-FRICTION-M0 — React UI reconciliation baseline

This report preserves the M0 before-state. The current fixture includes the
[subsequent stabilization round](cts-react-ui-stabilization-m1.md); its evidence
is stored separately and does not overwrite this baseline.

## Outcome

**Success for the first milestone:** three paired webpages run through their
real compiler and browser paths, the original website's card clipping is
reproduced, and the next reconciliation work is isolated with compiler and
numeric browser evidence. This milestone establishes the corpus and priorities;
it does not establish that Copeland frontend authoring is already easier than
ordinary TypeScript.

The motivating website failure is confirmed in a fresh current-compiler build:
all four feature cards clip their prose at 768px and 390px. The first tablet
card hides 41.5px of paragraph content; the first mobile card hides 28.5px.
The desktop cards at 1440px do not clip. Existing rectangle-containment checks
were insufficient because a card's rectangle can fit while its text is hidden.

The most valuable next milestone is projection fidelity in the existing
`LayoutJavaScriptProjectEmitter`: local streams currently discard declared gap
and padding, and a `fill` sibling shrinks a supposedly fixed-width region.
Changing the surface grammar first would preserve those underlying defects.

## Delivered specimens

The canonical fixture is [react-friction-m0](../../../samples/copeland-ts/react-friction-m0/README.md).
It contains a Copeland application and an ordinary React + TypeScript
application with shared verbatim text in `src/Content.ts`.

| Specimen | Real exercise | Relevant pressure |
|---|---|---|
| Article | Short/long copy, lists, links, unbroken identifier, scrollable code | Content growth, 390px width, 200% text size, alternate font |
| Issue triage | Select, insert fourth issue, change arrangement, search, empty/loading/error, retry | Repeated content, retained selection, collection limits, layout reuse |
| Settings | Empty-name validation, controlled input, valid save, real Base UI dialog | Typed callbacks, label semantics, wrapping, focus/Escape/reopening |
| Assigned-height negative witness | Same article content in a 180px slot followed by notes | Content overlaps the next region in both implementations |
| Existing Copeland website | Original authored card copy at 1440/768/390px | Real hidden-overflow card failure, independently measured |

Copeland is built by TSPack's managed target using the repository CLI and its
actual parser, binder, MIR, JavaScript emitter, local-stream projection,
component contracts, and browser realization. The counterpart passes strict
TypeScript checking and is bundled with esbuild. Both pages use the same
TSPack-materialized React/React DOM 19.2.7 singleton and Base UI 1.0.0-rc.0
dialog artifacts. Ordinary TS uses native React input events; Copeland uses a
small explicit contract for Base UI's real `Input.onValueChange(string, details)`.
The contracted adapter intentionally consumes only its first string argument.

The browser host is staged from the existing website runtime source into an
ignored local package artifact because TSPack rejects path dependencies that
escape the fixture workspace. No new host implementation, input DOM-reading
bridge, component registry, layout solver, or renderer is retained.

## Browser findings

The paired corpus produced 72 structured observations and 48 screenshots across
1280, 768, and 390 CSS pixels. Each implementation passed actual selection,
insertion, search, empty/loading/error/retry, validation/save, dialog focus,
Escape, focus return, reopening, and package Close-button checks. Both shared
short and long paragraphs match exactly. Console warnings/errors, page errors,
failed requests, and HTTP errors were absent in the successful runs.

Local `height: fit` is already useful: its projection emits `height: auto`,
and the article's following notes move below growing prose, including at 200%
root text size. This is evidence for the local browser-flow path; it does not
prove that every top-level resolved layout has browser intrinsic measurements.

| Requested layout fact | Copeland observation | Ordinary TS observation |
|---|---|---|
| 300px issue-list width, desktop | 234.41px | 300px |
| 300px issue-list width, tablet | 211.77px | 300px |
| Narrow issue arrangement | Remains lateral until explicitly changed; list becomes 164.86px | Media rule stacks the regions; list occupies 366px |
| Local `gap: 12px; padding: 16px` | Accepted by compiler; neither property appears in emitted local CSS | Ordinary CSS can express both |
| 180px assigned-height prose, desktop | 370.55px overlap with following notes | Same overlap |
| 180px assigned-height prose, mobile | 636.08px overlap with following notes | Same overlap |

The width defect is explained by emitted local CSS: the list receives
`width: 300px`, its sibling receives `width: 100%`, and the row uses flex with
default shrinking. Consequently the browser cannot preserve the declared
fixed width. This is a backend fidelity problem with concrete source and
measured output, not evidence that fixed width should acquire different meaning.

The fixed-height negative witness demonstrates a sizing-policy problem shared
by both implementations. The original cards compound it with `overflow: hidden`,
concealing prose. Readable body text needs growth or a deliberate reachable
scroll surface; fitting/ellipsis can serve intentionally bounded labels. Simply
adding clipping, smaller fonts, or a taller guessed card is not convergence.

## Ranked friction

| Rank | Finding and evidence | Next bounded action |
|---|---|---|
| P1 | Accepted local gap/padding disappear; fixed/fill width differs from the source declaration | Preserve local layout facts through the existing projection; qualify actual rectangles and spacing |
| P1 | Original tablet/mobile cards hide text; local `fit` reading flow succeeds | Establish readable prose sizing and declared bounded-text policies; extend checks from boxes to text visibility |
| P1 | `ReactNode[]` children report `COPE-REACT-0007`; variable stream collections report `COPE-STREAM-0008` | Define one keyed runtime collection binding through the existing stream/React seam; do not fabricate positional semantic slots |
| P1 | Native `<input>` is unsupported; `onChange` is contextualized as string and causes four diagnostics | Admit a bounded typed intrinsic input contract and one precise repair; preserve typed event/value adaptation |
| P2 | Imported enum event constructors inside explicit callbacks report `COPE-CALL-0017` | Check capture classification for static imported constructors; retain explicit capture for actual environment values |
| P2 | JSX text before interpolation loses its trailing space: `<p>Workspace: {name}</p>` emits `"Workspace:", name` | Preserve meaningful inline whitespace through existing text binding; current fixture uses a precomputed string |
| P2 | Wide/stacked issue streams repeat the two content bindings | Make arrangement reuse work with captured content/state; use existing layout/bind machinery as the baseline |
| P2 | Numeric React keys are typed as strings and report `COPE-REACT-0014` | Qualify stable key typing together with the collection capability |
| P3 | Managed browser builds need `tsconfig.tsx` and an existing project path; old website lacked these | Keep current ownership scaffolding runnable; document the frontend setup burden |
| P3 | A helper named like an enum case produced `COPE-MODULE-0007` across modules | Isolate qualified enum-case/module-private name classification; fixture adapters use `Send...` names |

The input fallback is explicit ecosystem consumption, not native input support.
Its contract lists id, type, value, and string callback props. The ten-probe
compiler corpus preserves accepted, rejected, and accepted-but-misprojected
cases. It includes an accepted separate `layout type` / `layout` / `bind`
example, so reusable layout is tested rather than assumed unavailable.

The diagnostic for a variable stream collection currently calls it structural
container content. That is weaker than saying runtime collection binding is
unsupported and supplying the closest permitted form. Runtime width also
produces the same dimension diagnostic twice. These are useful exact-repair
targets rather than reasons to relax typed semantics.

## Authoring accounting

Excluding blank/comment-only lines and shared content, the authored Copeland
application has 288 source lines against 175 for ordinary TS. Copeland has 19
explicit captures, 11 event adapter functions, five private streams, and a
73-line dependency/component-contract manifest. Wide/stacked layouts repeat
both list and detail binding sites; the ordinary TS component binds them once.

This is source accounting for these specimens, not a timed human usability
study or a universal complexity metric. Copeland's immutable record updates,
nominal events, explicit captures, and inspectable layout identities have real
semantic value. The corpus shows that the current frontend path adds enough
adaptation and duplication that a superiority claim would be premature.

The insertion exercise is deliberately bounded: Copeland activates one known
fourth row. Ordinary TS derives, filters, and maps an array with stable keys.
Successful fourth-row insertion does not qualify arbitrary runtime collections.

## Concrete syntax comparisons

The current local stream, as used by the runnable article:

```ts
function ArticlePage(state: AppState): ReactNode {
    stream ArticleSurface<0px, 0px> {
        width: fill;
        height: fit;
        heading: ArticleHeading() { height: fit; }
        body: ArticleBody(state) { height: fit; }
        notes: Notes() { height: fit; }
    }
    return ArticleSurface();
}
```

The accepted separate surface, demonstrated by the compiler corpus:

```ts
layout type Reading {
    column root {
        slot heading;
        slot body;
    }
}

layout Page<0px, 0px> satisfies Reading {
    width: 800px;
    height: 600px;
    column root {
        slot heading { height: 64px; }
        slot body { height: fill; }
    }
}

bind Page {
    heading: Heading();
    body: Body();
}
```

Two candidates for the subsequent authoring comparison are below. They are
**design sketches, not accepted syntax and not implemented in this milestone**.
Both preserve the explicit origin on the layout that owns geometry.

Candidate A separates contributions from an explicitly selected arrangement:
`ArticleReading` is a separately declared layout with its required
`<0px, 0px>` origin header.

```ts
stream ArticleContent {
    heading: ArticleHeading();
    body: ArticleBody(state);
    notes: Notes();
}

return bind ArticleReading using ArticleContent;
```

Candidate B retains the current inline stream shorthand while making a
reusable arrangement selectable:

```ts
stream ArticleSurface<0px, 0px> using ArticleReading {
    heading: ArticleHeading();
    body: ArticleBody(state);
    notes: Notes();
}
```

The comparison should ask whether changing arrangement leaves contribution
bindings, state, and event code unchanged; whether a layout contract diagnoses
missing/wrong channels precisely; and whether generated layout inspection still
explains each host. Shader `@location`, `@builtin`, and `@binding` metadata
qualify channel semantics. UI decorators should be introduced only for a
similarly necessary fact; this corpus does not justify moving all UI geometry
into decorators or making shader and UI grammar identical.

## Changes and validation

The only compiler change admits standard React `label`, `ul`, and `li`
intrinsics. Their ordinary native semantics are exercised by the compiled
browser specimens. No input event widening, capture-rule change, collection
runtime, new grammar, or layout behavior change landed. The original website
gained the compiler-owned `tsconfig.tsx` and `Website.csproj` needed to build
its current managed browser target; page source and style remain unchanged.

Validation on 2026-10-12:

- `dotnet build Copeland.slnx -m:1`: passed, zero warnings/errors.
- Full `dotnet test Copeland.slnx --no-build -m:1`: **2,113 passed**, zero failed/skipped.
- Strict baseline and TypeScript tooling checks: passed.
- Ten compiler probes: all accept/reject expectations matched.
- Current TSPack builds for new fixture and original website: passed.
- Paired browser corpus: 72 observations, matching copy, clean diagnostics.
- Original website regression: 12 cards measured; eight tablet/mobile cards clip.
- One-command `run.ps1`: rerun through the same source/build/browser path.

The browser evidence is Chromium-based Microsoft Edge 153.0.4234.48 controlled
by Playwright 1.56.1. It is not a cross-browser certificate. The text-size
exercise changes root font size to 32px; the alternate-font exercise selects
monospace and waits for `document.fonts.ready`. Browser zoom, network font
loading races, arbitrary component libraries, SSR, and hydration remain
unqualified. Loading/error are local state exercises, not HTTP failure proofs.

Tracked evidence:

- [Browser report](evidence/react-friction-m0/browser-report.json)
- [Original website regression](evidence/react-friction-m0/website-regression.json)
- [Compiler report](evidence/react-friction-m0/compiler-report.json)
- [Authoring accounting](evidence/react-friction-m0/authoring-accounting.json)
- [Full .NET validation](evidence/react-friction-m0/validation.json)

Reports retain source/artifact SHA-256 values where relevant. Screenshots are
generated under the fixture's ignored `artifacts/screenshots/` directory.
