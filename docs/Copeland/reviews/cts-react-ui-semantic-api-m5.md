# CTS-REACT-UI-SEMANTIC-API-M5

Issue triage now describes choices, notices, actions, search, and reading content
with immutable Copeland records. `IssuesPage.ts` contains no intrinsic HTML tags.
Rows are real runtime data: repeated insert, remove, filter, and reverse operations
produce immutable snapshots, and React reconciles them by stable identity.

This completes the first semantic API slice. The motivating improvement is that
the application describes what it presents and matches its states explicitly;
knowledge of `li`, `p`, and DOM event payloads is confined to the projection library.
It does not establish a universal source-size or developer-productivity advantage.

## Authoring and ownership

The library is ordinary compiled Copeland source in
[`UI.tsx`](../../../samples/copeland-ts/react-friction-m0/src/UI.tsx), alongside
[`Collections.ts`](../../../samples/copeland-ts/react-friction-m0/src/Collections.ts).
The application is in
[`IssuesPage.ts`](../../../samples/copeland-ts/react-friction-m0/src/IssuesPage.ts),
[`Issues.ts`](../../../samples/copeland-ts/react-friction-m0/src/Issues.ts), and
[`State.ts`](../../../samples/copeland-ts/react-friction-m0/src/State.ts).

| Description | Meaning | Projection |
|---|---|---|
| `ChoiceListSpec<Item, Event>` | Items, identity, label, selection, selection event | `PresentChoices` |
| `NoticeSpec<Event>` | Pending/information/failure, message, optional action | `PresentNotice` |
| `ActionSpec<Event>` | Label and typed application event | `PresentAction` |
| `SearchFieldSpec<Event>` | Label, value, typed edit conversion | Inside `PresentHeader` |
| `PageHeaderSpec<Event>` | Title, search description, actions | `PresentHeader` |
| `ReadingSpec` | Selected reading, summary, prose, related link | `PresentReading` |

For example, the application describes its choices without selecting a DOM tag:

```typescript
return PresentChoices<Issue, AppEvent>({
    label: "Issue list",
    heading: "Incoming issues",
    items: VisibleIssues(state.rows, state.query),
    identity: IssueIdentity,
    text: IssueLabel,
    select: SelectIssue,
    selected,
    emptyMessage: "No matching issues.",
}, send);
```

`IssueStatus` is an enum with `Loading`, `Failed(message)`, and `Ready`. Exhaustive
matches select the relevant description. `Option<int>` represents selection;
`Option<Issue>` resolves the selected row, and removal clears selection to `None`.
Actions carry event data. Edit/selection conversion and dispatch occur on browser
interaction, through existing explicit captures, rather than during construction.

The reducer owns domain state. React owns keyed child reconciliation. Base UI owns
the search control. Existing imported layout templates, `stream` contributions,
and `bind` own spatial arrangement. Runtime rows occupy one React contribution
inside the statically declared layout. No new grammar, renderer, state machine,
or DOM diff engine was introduced.

## Compiler/backend fixes required by the real specimen

- React TS-XML children admit immutable `ReactNode[]`; record arrays and nested
  node arrays remain rejected with `COPE-REACT-0007`.
- Local `MutableArray<ReactNode>` construction requires an explicit initializer.
  Uninitialized allocation remains rejected with `COPE-ARRAY-0008`. Generic filled
  arrays are checked again after specialization, so callable/array element types
  cannot bypass the existing storage boundary.
- Generic specialization preserves owner-bound templates, transitive generic
  helpers, captured arrows, React elements, mutable array operations, and closed
  Option cases. Open generic records are type-only exports. Generic parameters
  shadowing foreign private type names no longer trigger false privacy errors;
  actual use outside their scope still fails.
- Required external bindings follow imported generic specializations. Conflicting
  external imports receive `COPE-GENERIC-0010` with a rename repair instead of
  silently calling the wrong package. The current snapshot conservatively carries
  the owner's external imports; this is not a general alias-hygiene qualification.
- Nominal enum registrations are shared across ESM modules through the existing
  nominal-value registry. This fixes an enum constructed in the state module
  being rejected while validating the same record in another module.
- JavaScript mutable array assignment evaluates its right-hand value once.
  The new callback-count regression exposed the previous double invocation.

## Evidence and reproduction

Run from the repository root:

```powershell
./samples/copeland-ts/react-friction-m0/run.ps1 -FullValidation
```

The runner builds both pages through their real paths, checks ordinary TypeScript
and tooling, runs compiler specimens and Edge workflows, and closes ephemeral
servers/browser contexts. Its default evidence directory is
[`react-ui-semantic-api-m5`](evidence/react-ui-semantic-api-m5/).

- All **35 compiler specimens** match their expected acceptance or exact rejection.
  They include the maintained semantic library, missing enum case, Option match
  context, valid node children, and rejected storage/child shapes.
- **86 paired browser observations** at 1280, 768, and 390 pixels pass with matching
  copy and no page errors or console warnings/errors. Workflows include repeated
  insertion, identity-preserving reorder, keyboard selection, selected-row removal,
  filtering, draining the collection, and refilling without reusing identities.
  Existing article/text/resize/settings/dialog checks remain in the paired run.
- **Four focused library browser observations** prove construction does not send
  events; selection converts/sends once; duplicate/empty identities produce visible
  alerts without ambiguous React keys; optional retry actions send typed events.
- Original website regression: **12 card observations**, with no clipped prose.
- Full solution build passes with zero warnings/errors; all **2,188 .NET tests**
  pass across eight assemblies. Results, compiler hashes, and command evidence are
  recorded in [`validation.json`](evidence/react-ui-semantic-api-m5/validation.json).
  Ten additional .NET cases exercise cross-module enum/Option values, the real
  collection library's callback counts and frozen results, generic owner identity,
  privacy scope, and conflicting external bindings.

Source and emitted module hashes are recorded in the browser/compiler reports.
Screenshots remain in the sample's ignored `artifacts/react-ui-semantic-api-m5`
directory. Historical M0–M4 evidence retains its original milestone meaning.

## Source cost and remaining boundaries

Accounting excludes blank/comment lines and shared copy. Copeland's current
application is **395 lines**, plus **177 lines** of reusable UI/collection code:
**572 total**, against **194 lines** for the matched ordinary TypeScript fixture.
The semantic issue page itself is 92 lines with zero intrinsic tags. This is a
meaning/ownership improvement, not a claim of less total code. The library cost
is included rather than hidden outside the comparison.

This is a bounded prototype library, not a published UI SDK. Settings and article
authoring retain their earlier TS-XML forms. Full document composition, table
editing, optional document XML, and semantic dialog/forms remain later slices.
The reading description is a selected summary/prose/link, not a complete document
algebra. Runtime issues are immutable row arrays, not a new columnar table engine;
existing typed table/query output can feed this projection in a later specimen.

Identity is unique/nonempty within a choice collection and stable under reorder in
the same React parent. No preservation claim covers subtree remounts during layout
replacement or arbitrary component-local state. Action labels are unique in this
bounded static action list. Identity validation is quadratic and not qualified for
large or virtualized collections. Loading/error are authored domain states, not a
network lifecycle implementation. Dynamic Machina layout topology remains rejected.
