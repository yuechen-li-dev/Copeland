# API Coherence and Polish Audit (M8)

Date: 2026-05-11  
Repository: `MachinaLayout.JS`

## 1) Executive summary

This pass audited exports, type surfaces, docs, samples, and package outputs for coherence after M5–M7 expansion.

High-level result:

- Core runtime API is coherent and functionally aligned with current features (`RootFrame`, `GridArrange`, `GuideFrame`, variants, layers, interpolation).
- React and MachinaText adapter boundaries are mostly clear and remain non-authoritative for layout geometry.
- Build/test/sample/package verification all pass.
- Main coherence risks are documentation drift in `README.md` and minor terminology consistency gaps (not runtime/API defects).

No runtime behavior or public API changes were made in this audit pass.

---

## 2) Public API inventory

Inventory source: `src/index.ts` and nested barrels.

### 2.1 Core types

Exported via `export * from "./types"`:

- Identity/base: `NodeId`, `LayerName`, `Rect`
- Frame specs: `RootFrame`, `AbsoluteFrame`, `AnchorFrame`, `GuideFrame`, `FixedFrame`, `FillFrame`, `CellFrame`, `FrameSpec`
- Length/offset/reference: `UiLength`, `OffsetSpec`, `RectEdge`, `EdgeRef`, `GuideLength`
- Arrange specs: `StackAxis`, `StackJustify`, `StackAlign`, `EdgeInsets`, `StackArrange`, `GridTrack`, `GridArrange`, `ArrangeSpec`
- Authoring rows/variants: `LayoutVariantCondition`, `LayoutRowVariant`, `LayoutRow`, `LayoutNode`, `LayoutDocument`
- Resolved output: `ResolvedLayoutNode`, `ResolvedLayoutDocument`, `ResolvedLayoutTree`

Exported from errors module:

- `MachinaLayoutErrorCode`
- `MachinaLayoutError`

### 2.2 Core functions

Exported from root barrel:

- Compile/select:
  - `compileLayoutRows`
  - `selectLayoutRowsForRoot`
- Resolve:
  - `resolveFrame`
  - `resolveLayoutDocument`
  - `resolveLayoutRows`
- Helpers/utilities:
  - validation helpers (`assertFiniteNumber`, etc.) via `validation`
  - padding helpers via `padding`
  - length helpers (`resolveUiLength`) via `length`
  - offset helpers (`applyOffset`) via `offset`
  - tree conversion helpers (`toResolvedTree`, `flattenResolvedTree`)
  - formatting helper (`formatRect`)
- Interpolation:
  - `lerpNumber`
  - `lerpRect`
  - `lerpResolvedLayouts`

### 2.3 React adapter

Exported from `src/react/index.ts` and re-exported from root:

- Component: `MachinaReactView`
- Props types: `MachinaReactViewProps`, `MachinaSlotProps`

Observed adapter-facing concepts in type surface/tests/docs:

- layer policy + named layer ordering
- slot/view rendering key (`view ?? slot` semantics)
- adapter data channels (`viewData`, `nodeData`)
- containment/content-visibility knobs

### 2.4 MachinaText

Exported from `src/text/index.ts` and `src/text/react/index.ts`:

- Source/spec/domain types:
  - `MachinaTextSource`, `MachinaTextSpec`, `MachinaTextDocument`
  - policy enums/aliases (`MachinaTextVariant`, `MachinaTextWrap`, etc.)
- AST/content types:
  - `MachinaTextBlock`, `MachinaInline`, `MachinaBulletItem`
- Diagnostics:
  - `MachinaTextDiagnostic`, `MachinaTextDiagnosticCode`, `MachinaTextDiagnosticLevel`
  - `ParseMachinaTextResult`
- Parser:
  - `parseMachinaText`
  - `parseMachinaTextInline`
- React renderer:
  - `MachinaTextView`
  - `MachinaTextViewProps`

### 2.5 Public API coherence findings

- No clearly accidental/internal export was found in root barrel; exports are broad but intentional for current tests/docs usage.
- No documented major API appears missing from exports.
- A few exports (`formatRect`, `flattenResolvedTree`) are minimally documented in top-level README (discoverability issue, not API bug).
- Documentation is less explicit than code about root barrel re-exporting adapters (`react`, `text`) and may benefit from a short API index section.

---

## 3) Concept taxonomy

Current taxonomy can be expressed cleanly as:

1. **Node identity + hierarchy**
   - `id`: stable node key
   - `parent`: coordinate owner relationship
   - `order`: sibling sequence within parent

2. **Frame = node rectangle acquisition**
   - `RootFrame`: root-only ownership from `rootRect`
   - `AbsoluteFrame`: explicit parent-local rect
   - `AnchorFrame`: edge/size constraints with `UiLength`
   - `GuideFrame`: reference-assisted placement using resolved targets
   - `FixedFrame`: stack-child explicit size
   - `FillFrame`: stack-child weighted main-axis fill
   - `CellFrame`: grid-child explicit track coordinates/spans

3. **Arrange = direct child placement**
   - `StackArrange`: 1D ordered arithmetic placement
   - `GridArrange`: 2D explicit track/cell placement

4. **Node metadata**
   - semantic/view keys: `view`, `slot`
   - diagnostics label: `debugLabel`
   - paint ordering: `z` (bounded sibling-local), `layer` (named semantic layer)
   - post-placement nudge: `offset`

5. **Authoring-time row features**
   - `variants` on rows only, selected before compile/resolve

6. **Resolved output**
   - flat: `ResolvedLayoutDocument`
   - nested projection: `ResolvedLayoutTree`

7. **Renderer adapters**
   - React adapter consumes resolved rectangles and paints wrappers in DOM

8. **Text subsystem**
   - parser + diagnostics (MachinaText)
   - React text renderer (`MachinaTextView`) inside already-owned rectangles

Taxonomy readability in current names is generally good; primary ambiguity is contextual validity of some frame kinds (`Fixed`/`Fill`/`Cell`/`Guide`) which relies on docs + errors rather than type-level separation.

---

## 4) Naming consistency audit

### 4.1 `Frame` vs `Arrange`
- Consistent and clear: frame answers “this node rect,” arrange answers “direct child placement.”

### 4.2 `FillFrame` vs `GridTrack { kind: "fill" }`
- Potential overload but acceptable: both represent weighted distribution semantics in different contexts.
- Recommend doc callout clarifying **node-level fill (stack child)** vs **track-level fill (grid template)**.

### 4.3 `CellFrame`
- Reads as grid-only and aligns with behavior/erroring (`CellFrameWithoutGrid` / `GridChildMustBeCell`).

### 4.4 `GuideFrame`
- Mostly clear from reference docs; name can be interpreted as “helper reference” and does communicate non-ownership placement.
- Recommend one explicit phrase in top README: “GuideFrame reads other nodes; it does not reparent or portal.”

### 4.5 `view` vs `slot`
- Code/docs consistently use `view ?? slot` semantics.
- `slot` remains valid and adapter-facing; not marked legacy.
- Some top-level README examples still use only `slot`, reducing emphasis on preferred `view` naming.

### 4.6 `layer` vs `z`
- Coherent: semantic grouping (`layer`) then local order (`z`).

### 4.7 `OffsetSpec` and `UiLength`
- Names are coherent and sufficiently neutral (not overly CSS-like).

### 4.8 `variants`
- Reasonable name; docs mention responsive variants phase separation.

### 4.9 `lerpResolvedLayouts`
- Name is explicit and consistent with accompanying `lerpNumber`/`lerpRect`.

### 4.10 `MachinaText*`
- Consistent prefixing across parser, AST types, diagnostics, and renderer.

---

## 5) Type consistency audit

### 5.1 Context-restricted frames

Status:
- `FixedFrame` and `FillFrame` stack-only: enforced at runtime/compile and documented.
- `CellFrame` grid-only: enforced and documented.
- `GuideFrame` document-level resolver only: `resolveFrame` throws `GuideTargetUnresolved` with explicit guidance.
- `RootFrame` root-only: compile rejects non-root root frame.

Coverage:
- Restrictions are covered by tests (`resolveFrame`, `compileLayoutRows`, `gridArrange`, `guideFrame`).

### 5.2 Metadata preservation

Checked `LayoutRow`, `LayoutNode`, `ResolvedLayoutNode`, `ResolvedLayoutTree`:
- `view`, `slot`, `debugLabel`, `z`, `layer`, `offset` are present and threaded consistently where appropriate.

### 5.3 Authoring-only fields

- `variants` exists only on `LayoutRow` and is absent from compiled/resolved node types.
- This matches intended phase separation.

### 5.4 Adapter-only props

- `viewData`, `nodeData`, `layers`, containment controls are in React adapter props surface, not in core layout types.

### 5.5 Text type isolation

- Text parser/renderer types are scoped under `src/text/*` and do not pollute core layout model types.

Finding: type surface is coherent; no corrective change needed in M8.

---

## 6) Error code audit

### 6.1 `MachinaLayoutErrorCode` inventory by domain

- **Compile/row structure**: `EmptyRows`, `MissingRoot`, `MultipleRoots`, `DuplicateId`, `InvalidId`, `UnknownParent`, `SelfParent`, `Cycle`, `UnreachableNode`, `RootFrameNotRoot`
- **Numeric/validation**: `NonFiniteNumber`, `NegativeSize`, `NegativeGap`, `NegativePadding`, `InvalidLengthUnit`, `InvalidZ`
- **Anchor/frame resolution**: `InvalidAnchorHorizontal`, `InvalidAnchorVertical`, `NegativeResolvedSize`, `RootFrameWithoutRoot`
- **Stack**: `FixedFrameWithoutArranger`, `FillFrameWithoutArranger`, `InvalidFillWeight`, `StackChildMustBeFixed`, `StackContentNegative`, `StackOverflow`
- **Grid**: `CellFrameWithoutGrid`, `GridChildMustBeCell`, `InvalidGridTrack`, `InvalidGridCell`, `GridContentNegative`, `GridOverflow`
- **Guide/reference**: `GuideTargetNotFound`, `GuideSelfReference`, `GuideReferenceCycle`, `GuideInvalidEdgeForAxis`, `GuideTooManyReferencesPerAxis`, `InvalidGuideFrame`, `GuideTargetUnresolved`
- **Variants**: `InvalidVariantCondition`
- **Interpolation**: `IncompatibleLayouts`

### 6.2 Specificity/misuse/dead code check

- Codes are sufficiently specific for API-facing debugging.
- No declared layout error codes found obviously dead/unreachable in this pass.
- One naming sharp edge: `StackChildMustBeFixed` now allows `FillFrame` too; message string says fixed-or-fill, but code name is legacy-biased and slightly misleading.
  - Recommendation: keep code for compatibility, improve docs note mapping.

### 6.3 MachinaText diagnostics audit

Declared codes:
- `unsupported_syntax`
- `heading_forbidden`
- `max_list_depth_exceeded`
- `malformed_link`
- `unclosed_inline`
- `invalid_escape`

Emitted paths inspected in parser:
- all declared codes are emitted in concrete branches.
- no obvious dead diagnostic code found (including `invalid_escape`, previously risky area).

Docs coverage:
- `docs/machina-text-parser.md` currently emphasizes escape behavior; broader code taxonomy documentation is thin.

---

## 7) Docs/examples consistency audit

Inspected: `README.md`, `docs/*.md`, sample READMEs.

### 7.1 Strong points
- Frame/arrange boundaries and restrictions documented in focused docs (`frames-and-stack`, `grid-arrange`, `reference-alignment`).
- Explicit boundary statements (“Machina places, CSS paints”) are present across docs.
- MachinaText ellipsis single-line policy documented.

### 7.2 Drift/inconsistency findings

1. **Top-level README still branded around “M0 scope” language** despite including later features; this can confuse maturity/state.
2. README docs index omits some key current docs from the main list (e.g., text docs are less discoverable in the principal section structure).
3. README quick examples rely mostly on `slot` terminology; current guidance prefers `view` with slot fallback.
4. README includes note “legacy root Absolute/Anchor remain accepted” (in deeper docs) which is okay for compatibility but should be cross-linked from root section for clarity.

No runtime-impacting docs contradiction found for:
- root via `RootFrame`
- stack-only fixed/fill
- grid-only cell
- guide reference semantics
- negative positional anchors allowed
- variants phase separation
- named layers non-portal semantics
- no intrinsic text sizing / no CSS geometry authority

---

## 8) Sample usage audit

Inspected `samples/control-room` and `samples/music-player` plus builds.

Findings:
- Both samples build successfully.
- Control-room README reflects modern concepts (`view ?? slot`, `UiLength`, `offset`, stable views, text view usage).
- Music-player README local file dependency instructions are accurate.
- No obvious sample pattern teaching unstable inline view factories.
- No evidence samples depend on CSS layout for Machina node placement authority.
- `package.json` `files` excludes `samples/` from npm tarball already (good).

No required sample code updates for M8.

---

## 9) Package export/build audit

### 9.1 Export map/build outputs
- `package.json` exports only `.` with `types` and `import` pointed to `dist/index.d.ts` and `dist/index.js`.
- Build emits matching files in `dist/`.
- Peer dependencies retain React/ReactDOM as peer deps (correct for adapter).

### 9.2 Tarball contents (`npm pack --dry-run`)
- Includes: `dist`, `README`, `LICENSE`, `docs`, `package.json`.
- Excludes: `src`, `test`, `samples` (expected and desirable).

### 9.3 Dist/API coherence
- Dist typings generated successfully.
- Public root import surface validated by existing public API tests and successful build/test pipeline.

No package/export corrections required in this pass.

---

## 10) Proposed one-page mental model

> MachinaLayout in one page

1. **Author layout as flat rows (`LayoutRow[]`)**.  
   Each row is a node with `id`, optional `parent`, and optional `order`.

2. **Parent means coordinate ownership**.  
   A node’s rectangle is resolved in its parent’s coordinate space.

3. **Frame decides this node’s rectangle**.  
   Use one frame kind per node (`root`, `absolute`, `anchor`, `guide`, `fixed`, `fill`, `cell`) according to context.

4. **Arrange decides direct child placement**.  
   Parent `arrange` (`stack` or `grid`) places only immediate children.

5. **Metadata shapes rendering behavior, not geometry solving**.  
   `view/slot` map nodes to renderer views; `layer` + `z` control paint order; `offset` nudges after placement.

6. **Variants are authoring-time selection**.  
   `variants` are chosen against root size before compile/resolve.

7. **Resolver outputs deterministic rectangles**.  
   Core output is a resolved document/tree of numeric rects.

8. **Adapters render rectangles**.  
   React adapter paints absolute wrappers and injects node/view data.

9. **Text renders inside owned rectangles**.  
   MachinaText parses/paints content inside a rectangle; it does not drive outer layout sizing.

10. **CSS paints; Machina places**.  
    CSS can style internals, but Machina remains geometry authority for node placement.

---

## 11) Risks / confusing areas

1. **README lifecycle terminology drift** (“M0” framing with later feature reality).
2. **Error-code naming legacy**: `StackChildMustBeFixed` includes fill children now.
3. **Discoverability gap** for full API index and text docs from root README.
4. **Contextual frame validity is runtime-validated**, not type-encoded; this is intentional but requires clear docs examples.

---

## 12) Recommended M8 cleanup checklist

Concrete follow-up checklist based on findings:

- [ ] Refresh top-level README scope/lifecycle section to reflect current post-M7 capability set (remove or clearly contextualize old “M0 scope” framing).
- [ ] Add a compact “Public API index” in README (core / react / text) mapping names to docs.
- [ ] Prefer `view` in top README examples while retaining explicit `view ?? slot` compatibility note.
- [ ] Add short README callout: `GuideFrame` references resolved geometry and does not reparent/portal.
- [ ] Add error-code reference table in docs (code → when emitted), including note that `StackChildMustBeFixed` means fixed-or-fill for backward compatibility.
- [ ] Add one lightweight smoke test that imports from built `dist` in a temporary fixture (optional but useful for publish confidence).

No runtime/API changes required by this audit.

---

## 13) Exact verification commands run

From repository root unless noted:

1. `npm test`
2. `npm run build`
3. `(cd samples/control-room && npm run build)`
4. `(cd samples/music-player && npm run build)`
5. `npm pack --dry-run`

All commands completed successfully in this environment.
