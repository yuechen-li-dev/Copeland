# MachinaText M2a Planning & Audit

## Status note (M2c.1)

M2c.1 is implemented in React renderer scope: explicit `leading`, `blockGap`, `listGap`, and `valign` policy fields now control vertical rhythm inside owned rectangles without changing layout resolver APIs or parser syntax.

## 1) Executive summary

MachinaText should be introduced as a **small, explicit text-content model** for authoring predictable text structures inside already-resolved Machina rectangles, without turning Machina into a document engine, Markdown engine, or font/layout measurement system.

For M2a, the least disruptive path is:

- add a **docs-first plan** (this file),
- keep MachinaText **independent from layout core resolution**,
- keep `LayoutRow` and resolver APIs unchanged,
- attach text policy at the **view payload layer** (application/renderer side) first,
- defer parser/runtime code to later milestones.

## 2) Goals

- Provide a brutally limited, machine-authorable text format for LLM-friendly edits.
- Keep text content model renderer-agnostic.
- Preserve Machina contract: layout owns rectangles, text stays inside resolved rectangles.
- Support a narrow set of content primitives:
  - plain text
  - paragraph breaks
  - strong
  - emphasis
  - inline code
  - links
  - unordered bullets with max depth 2
- Make policy explicit (variant/wrap/overflow/align) rather than hidden in syntax tricks.

## 3) Non-goals

- No full Markdown compatibility.
- No headings in M2.
- No images/tables/raw HTML/blockquotes/ordered lists/task lists/footnotes.
- No routing/dispatch/state behavior.
- No rich text editor concerns (selection/caret/editing model).
- No glyph shaping/font fallback/bidi engine in Machina core.
- No intrinsic sizing or text-driven outer layout changes.

## 4) Proposed package/module placement

### Audit observations

- Core public API is exported from `src/index.ts`, and currently exports geometry + react adapter modules only. `src/index.ts`.
- Core types (`LayoutRow`, `FrameSpec`, resolved docs) are geometry-oriented and intentionally small. `src/types.ts`.
- React integration currently maps `view ?? slot` keys to app-provided components via `views` registry; this is the existing integration seam for payload rendering. `src/react/MachinaReactView.tsx`.

### Recommendation (least disruptive M2 path)

1. **Add `src/text/` in same package** for M2-series incubation (types/contracts/docs-aligned naming).
2. Keep React-specific text helpers under **`src/text/react/`** (not mixed into current generic `src/react` root) to keep text concerns modular.
3. Re-export from root only when implementation starts (M2b+), not required for M2a docs-only.
4. Consider separate package later (`@machina/text`) only after API stabilizes and at least one non-React renderer exists.

Rationale: this keeps churn low, avoids premature package splitting, and preserves current adapter surface.

## 5) Proposed public type surface

> M2a recommendation: define these in plan only; do not implement yet.

```ts
export type MachinaTextSource =
  | { kind: "plain"; text: string }
  | { kind: "machina-text"; text: string };

export type MachinaTextVariant = "body" | "label" | "caption" | "title" | "mono";
export type MachinaTextWrap = "word" | "none";
export type MachinaTextOverflow = "clip" | "ellipsis" | "scroll";
export type MachinaTextAlign = "start" | "center" | "end";

export type MachinaTextSpec = {
  kind: "text";
  source: MachinaTextSource;
  variant?: MachinaTextVariant;
  wrap?: MachinaTextWrap;
  overflow?: MachinaTextOverflow;
  align?: MachinaTextAlign;
};
```

AST recommendation:

```ts
export type MachinaTextDocument = {
  blocks: MachinaTextBlock[];
};

export type MachinaTextBlock =
  | { kind: "paragraph"; inline: MachinaInline[] }
  | { kind: "bulletList"; items: MachinaBulletItem[] };

export type MachinaBulletItem = {
  inline: MachinaInline[];
  children?: MachinaBulletItem[];
};

export type MachinaInline =
  | { kind: "text"; text: string }
  | { kind: "strong"; children: MachinaInline[] }
  | { kind: "emphasis"; children: MachinaInline[] }
  | { kind: "code"; text: string }
  | { kind: "link"; href: string; children: MachinaInline[] };
```

Type-shape decisions:

- `strong` / `emphasis` should contain **children**, not raw text, to allow nested emphasis/code/link segments while keeping one consistent inline model.
- `link` should contain **inline children**, not label-only text, for parity and explicit structure.
- Bullets should allow nested inline formatting (same `MachinaInline[]` as paragraphs).
- Bullet max depth should be enforced by parser validation/diagnostics, not encoded in types.
- `variant/wrap/overflow/align` belong in `MachinaTextSpec` (content policy contract), while renderers decide exact visual implementation details.
- `plain` source should normalize into one-paragraph AST during parse.

## 6) Allowed syntax

MachinaText syntax (M2 target):

- plain text
- paragraph breaks (blank line separation)
- `**strong**`
- `*emphasis*`
- `` `code` ``
- `[label](href)` links
- unordered bullets (`-` prefix)
- nested bullets max depth 2

## 7) Forbidden syntax

- headings (`#`, `##`, etc.)
- ordered lists (`1.`)
- task list checkboxes
- blockquotes (`>`)
- fenced code blocks
- images (`![...]`)
- tables
- raw HTML
- extension syntax / custom directives
- CSS classes/styles inside text syntax
- any layout declarations
- route/action/dispatch syntax

## 8) Heading decision and rationale

**Decision: headings are explicitly out of scope in M2.**

Rationale:

- Heading markers smuggle hidden policy (typography scale, spacing, semantic levels).
- MachinaText should keep policy explicit and inspectable.
- “Title-like” rendering should come from explicit fields (`variant: "title"`) or explicit views, not punctuation shortcuts.

## 9) Bullet list decision and max-depth rationale

Decision:

- support unordered bullets only,
- allow nesting up to depth 2,
- reject/diagnose deeper nesting.

Rationale:

- Depth cap preserves predictable rendering across hosts.
- Prevents gradual drift toward document-authoring complexity.
- Meets common UI copy needs (short status/checklist-like structure) without introducing full markdown list semantics.

## 10) Parser behavior recommendation

Recommendation:

- Parser should return a **result object with diagnostics**, not throw for normal authoring errors.
- Hard throw only for programmer misuse (invalid API call types), not syntax mistakes in content strings.

Suggested shape:

```ts
export type MachinaTextDiagnosticLevel = "error" | "warning";

export type MachinaTextDiagnostic = {
  code:
    | "unsupported_syntax"
    | "heading_forbidden"
    | "max_list_depth_exceeded"
    | "malformed_link"
    | "unclosed_inline"
    | "invalid_escape";
  message: string;
  index: number;
  length: number;
  line: number;
  column: number;
  level: MachinaTextDiagnosticLevel;
};

export type ParseMachinaTextResult = {
  ok: boolean;
  document: MachinaTextDocument;
  diagnostics: MachinaTextDiagnostic[];
};
```

Fallback policy:

- Unsupported constructs should be preserved as literal text where possible, with diagnostics.
- Optional strict mode can fail `ok=false` on first unsupported construct.

## 11) Renderer boundary

Core MachinaText model owns:

- content AST semantics,
- small text policy flags (`variant`, `wrap`, `overflow`, `align`),
- parser diagnostics contract.

React/DOM renderer owns:

- actual text rendering via browser engine,
- mapping policy flags to CSS/DOM behavior,
- link interaction callbacks passed from app,
- accessibility annotations specific to host components.

Future non-DOM renderer owns:

- platform-specific shaping/layout internals,
- equivalent wrap/clip/ellipsis/scroll behavior inside rectangle,
- platform accessibility mapping.

## 12) Layout boundary

Hard boundary:

- MachinaLayout resolves outer rectangles.
- MachinaText operates inside resolved rectangle only.

Not allowed:

- requesting larger parent/sibling/root sizes,
- intrinsic measurement feedback into resolver,
- creating new layout nodes from text blocks.

Allowed:

- in-rectangle wrap/clip/ellipsis/scroll policy.

## 13) Link behavior boundary

Links are text semantics only:

- link node stores `href` + label children,
- no route syntax,
- no command dispatch tables,
- no state transition contract in MachinaText.

Application/renderers decide click behavior.

## 14) Suggested M2 milestone decomposition

- **M2a (this pass):** audit + plan doc + agreed type contracts (docs only).
- **M2b:** implement parser + diagnostics + unit tests (no renderer).
- **M2c:** React/DOM rendering adapter for MachinaText AST inside node views.
- **M2d:** sample integration in control-room-style demo with explicit text specs.
- **M2e:** docs polish, migration notes, edge-case hardening, non-DOM renderer contract notes.

## 15) Risks and mitigations

1. **Heading semantics creep**  
   Mitigation: explicit prohibition + diagnostic code `heading_forbidden`.

2. **Markdown dialect creep**  
   Mitigation: closed grammar and “unsupported as literal+diagnostic” policy.

3. **Text measurement creep into core**  
   Mitigation: hard boundary that no parser/model API takes font metrics or container reflow callbacks.

4. **Routing/event creep via links**  
   Mitigation: keep links as `href` semantics only, no action ids.

5. **HTML passthrough pressure**  
   Mitigation: explicit ban with diagnostics; no raw HTML node type.

6. **Intrinsic sizing pressure**  
   Mitigation: tests/docs asserting no resolver API change and no text->layout feedback path.

7. **Accessibility overpromises in core**  
   Mitigation: document that host renderers/apps own semantics and interaction behavior.

8. **Renderer leakage into core types**  
   Mitigation: keep core text types free of CSS/DOM-specific properties.

## 16) Recommended next implementation prompt

“Implement M2b parser-only MachinaText foundation (no renderer):

- Add `src/text/types.ts` and `src/text/parseMachinaText.ts`.
- Implement only allowed syntax: paragraphs, strong/emphasis/code/link, unordered bullets depth<=2.
- Return `ParseMachinaTextResult` with diagnostics; no throws for authoring errors.
- Forbid headings/raw HTML/ordered lists/etc. with explicit diagnostic codes.
- Add focused Vitest coverage for success and failure cases.
- Do not modify layout resolver APIs, `LayoutRow`, or React adapter behavior yet.”

---

## Repo-specific audit notes

- Current core shape is layout-only and should remain unchanged for M2a. `src/types.ts`.
- Current React integration seam (`view ?? slot` mapping) is a suitable place for app-level text view components in early milestones, avoiding immediate core API expansion. `src/react/MachinaReactView.tsx`.
- Current docs already enforce similar anti-creep guardrails (no intrinsic sizing/DOM authority), and MachinaText should align with that philosophy. `docs/forbidden-concepts.md`.

## M2b status note (implemented)

M2b parser-only foundation is now implemented in `src/text`:

- public MachinaText types are exported,
- `parseMachinaText` returns AST + diagnostics,
- no renderer integration added,
- no layout resolver API changes made.
