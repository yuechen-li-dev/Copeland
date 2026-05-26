# Reference-based alignment design contract (M7a)

## 1) Executive summary

This contract proposes a **narrow, explicit, Machina-native** reference alignment feature for M7b that solves real authoring pain (edge-following and attachment) **without** turning MachinaLayout into a general constraint solver.

**Recommendation (staged):**

- **M7b-1 (primary): `GuideFrame`** for one-axis/two-axis edge-based alignment with strict limits.
- **M7b-2 (optional follow-up): `AttachFrame`** for tooltip/popover point-to-point attachment if user demand remains high after GuideFrame ships.

Why staged: `GuideFrame` directly solves “start after inspector edge” (the most common structural pain), preserves existing `AnchorFrame` simplicity, and keeps dependency behavior isolated in one new frame kind.

Core invariant remains explicit and non-negotiable:

- **parent = coordinate owner**
- **reference/guide/target = read-only alignment input**

A reference is never ownership, traversal, state inheritance, event ownership, data ownership, or semantic DOM ownership.

## 2) Actual customer/user pain

The core pain is **authoring drift**: relationships between nodes are expressed as duplicated constants rather than declarative geometry dependencies.

Typical failures today:

- “toolbar starts after inspector” encoded as magic numbers (`left: 312`) that break when inspector width changes.
- tooltip/popover placements manually recomputed in userland and desynchronized from resolved layout.
- badge/corner alignment repeated across breakpoints with brittle arithmetic.

Users are not asking for global solver behavior; they are asking for **read-only edge following** and **targeted attachment** while preserving deterministic layout.

## 3) Non-goals

M7b must not introduce:

- a general linear/nonlinear constraint system,
- width/height references to arbitrary nodes (v1),
- cross-cutting dependency semantics in `AnchorFrame`,
- ownership ambiguity (multi-parent semantics),
- adapter-level geometry solving,
- hidden auto-portals/clipping escapes.

## 4) Existing features that already solve some cases

Current primitives already solve many structural layouts:

- `StackArrange + FillFrame` for directional allocation,
- `GridArrange + CellFrame` for regular 2D placement,
- responsive row variants for breakpoint-specific frame switching,
- `OffsetSpec` for post-placement nudging,
- named `layer` + sibling `z` for paint ordering.

These do **not** fully solve cross-node edge following for floating/overlay alignment.

## 5) Candidate designs

### Candidate A — Edge refs inside `AnchorFrame`

**Verdict: reject for M7b v1.**

Pros: direct syntax, no new frame kind.

Cons:

- pollutes the simplest parent-local frame with graph semantics,
- mixes “simple anchor math” and “reference dependency” in one type,
- increases risk of accidental multi-source constraints,
- weakens mental model and pushes toward solver creep.

### Candidate B — `AttachFrame`

**Verdict: good, but secondary.**

Excellent for tooltips/popovers/badges; constrained and clear (single target, point mapping). Less natural for one-axis “start after edge” content-region layouts.

### Candidate C — `GuideFrame` / `ReferenceAnchorFrame`

**Verdict: recommend for M7b-1.**

Best fit for structural edge-following while preserving `AnchorFrame` purity. Can reuse anchor-like axis rules with bounded dependency semantics.

### Candidate D — Named guides

**Verdict: defer.**

Potentially useful semantic layer, but introduces additional naming/export surface and lookup complexity before baseline semantics are proven.

### Candidate E — Do nothing

**Verdict: reject.**

Fails legitimate authoring pain for floating and edge-following relationships; forces brittle duplicated arithmetic.

### Additional candidate (F) — “Attach only” in M7b

**Verdict: plausible alternative but incomplete.**

Safer than Anchor refs, but does not cleanly solve the one-axis “region starts after another region edge” use case.

## 6) Recommended design

### M7b scope recommendation

Ship **`GuideFrame` only** in M7b, with strict constraints. Defer `AttachFrame` to M7c unless needed immediately.

Rationale:

1. Solves the broadest real pain (`left = target.right + offset`) with low surface area.
2. Keeps `AnchorFrame` unchanged and parent-local.
3. Limits dependency complexity to one explicit frame kind.
4. Avoids overcommitting to two new models in one release.

If product requires tooltip-first workflows immediately, an acceptable variant is:

- M7b: `GuideFrame`
- M7b.1: `AttachFrame` (additive, same dependency engine)

## 7) Type proposal

```ts
export type RectEdge =
  | "left"
  | "right"
  | "top"
  | "bottom"
  | "centerX"
  | "centerY";

export type EdgeRef = {
  ref: NodeId;
  edge: RectEdge;
  offset?: UiLength; // resolves on referencing node's parent axis (see semantics)
};

export type GuideLength = UiLength | EdgeRef;

export type GuideFrame = {
  kind: "guide";
  left?: GuideLength;
  right?: GuideLength;
  top?: GuideLength;
  bottom?: GuideLength;
  width?: UiLength;
  height?: UiLength;
};
```

Validation limits (v1):

- Exactly two horizontal constraints from `{ left, right, width }`.
- Exactly two vertical constraints from `{ top, bottom, height }`.
- `width/height` are `UiLength` only (no refs).
- Horizontal keys (`left/right`) may reference only `left/right/centerX`.
- Vertical keys (`top/bottom`) may reference only `top/bottom/centerY`.
- **At most one `EdgeRef` per axis** (horizontal max 1, vertical max 1).
- Self-reference forbidden (`ref === current node id`).

(Explicitly no `AnchorFrame` changes in v1.)

## 8) Semantics

### Ownership semantics

- Logical parent still owns coordinate system and hierarchy.
- Reference target is geometry input only.

### Coordinate space

- Resolved rectangles remain global/root-space.
- `EdgeRef` reads target edge in global/root-space.
- `UiLength` inside `EdgeRef.offset` resolves against the **referencing node’s logical parent axis**:
  - horizontal edge refs use parent width axis,
  - vertical edge refs use parent height axis.

Reason: keeps `ui` behavior consistent with existing parent-owned coordinate semantics.

### Placement semantics

- Compute numeric values for each present field:
  - If field is `UiLength`, resolve in parent axis as today.
  - If field is `EdgeRef`, evaluate target edge in global space and add resolved `offset` (default 0).
- Solve axis exactly like anchor math once concrete numbers are known.
- Apply node-level `offset` via existing post-placement path (`applyOffset`) after frame solve.
- Final rect may lie partially/fully outside logical parent (allowed).

## 9) Dependency model

Use a **restricted dependency pass for reference frames only**.

Dependencies for a `GuideFrame` node:

- its logical parent must be resolved,
- each referenced target node must be resolved.

Algorithm:

1. Resolve all non-guide nodes via existing traversal path.
2. Collect unresolved guide nodes.
3. Iteratively resolve guide nodes whose dependencies are satisfied.
4. Repeat until no progress.
5. Remaining unresolved guide nodes -> classify as cycle vs missing/unresolvable target.

This is not a general solver:

- no symbolic equation balancing,
- no iterative numeric relaxation,
- no width/height references,
- bounded, acyclic dependency requirement.

## 10) Validation/error model

Add explicit error codes (separate from parent-cycle errors):

- `GuideTargetNotFound`
- `GuideSelfReference`
- `GuideReferenceCycle`
- `GuideInvalidEdgeForAxis`
- `GuideTooManyReferencesPerAxis`
- `InvalidGuideFrame`
- `GuideTargetUnresolved`

Notes:

- Parent graph cycle remains existing parent-cycle error.
- Reference-cycle is distinct and must report involved node ids.
- `GuideTargetUnresolved` covers cases where target exists but cannot resolve due to its own invalid frame/dependency chain.

## 11) Interaction with existing features

- **StackArrange:** direct stack children remain `FixedFrame`/`FillFrame` only. `GuideFrame` as direct stack child is invalid (reuse existing stack child-frame validation path).
- **GridArrange:** direct grid children remain `CellFrame` only. `GuideFrame` as direct grid child invalid (reuse existing grid child-frame validation path).
- **Responsive variants:** variant selection occurs first; whichever frame is selected is validated/resolved. Switching between `anchor` and `guide` is supported.
- **OffsetSpec:** applied after guide placement (single source of post-placement nudge).
- **Named layers:** paint order only; no impact on guide geometry.
- **Future portals:** unaffected; references do not imply reparenting.
- **`lerpResolvedLayouts`:** unchanged; interpolates resolved rectangles regardless of source frame kind.
- **React adapter:** no geometry logic changes required; existing parent-local normalization remains.

## 12) Examples

### A) Start after inspector edge

```ts
{
  id: "main-toolbar",
  parent: "root",
  frame: {
    kind: "guide",
    left: { ref: "inspector", edge: "right", offset: 8 },
    right: 16,
    top: 16,
    height: 48,
  },
}
```

### B) Tooltip below button (with node-level offset)

Using `GuideFrame` approximation in M7b (true point attachment deferred):

```ts
{
  id: "help-tooltip",
  parent: "overlay-root",
  frame: {
    kind: "guide",
    left: { ref: "help-button", edge: "left" },
    width: 220,
    top: { ref: "help-button", edge: "bottom" },
    height: 64,
  },
  offset: { x: 0, y: 6 },
}
```

### C) Badge at card corner

```ts
{
  id: "card-badge",
  parent: "root",
  frame: {
    kind: "guide",
    right: { ref: "card-42", edge: "right" },
    width: 18,
    top: { ref: "card-42", edge: "top" },
    height: 18,
  },
  offset: { x: 6, y: -6 },
}
```

### D) Responsive variant

```ts
{
  id: "context-toolbar",
  parent: "root",
  frame: { kind: "anchor", left: 16, right: 16, top: 72, height: 44 },
  variants: [
    {
      when: { minWidth: 1024 },
      frame: {
        kind: "guide",
        left: { ref: "inspector", edge: "right", offset: { unit: "ui", value: 0.01 } },
        right: 16,
        top: 16,
        height: 44,
      },
    },
  ],
}
```

## 13) M7b implementation plan

1. Add `GuideFrame`, `RectEdge`, `EdgeRef` types (public API surface only as contract defines).
2. Add validation rules and new error codes for guide semantics.
3. Add edge-evaluation helper (`getRectEdgeValue`) + `GuideLength` resolver.
4. Extend document resolver with restricted guide dependency pass.
5. Keep `resolveFrame` behavior explicit:
   - either reject `guide` in direct frame resolver with explicit guard,
   - or route through document-level resolver path only.
6. Ensure resolved output schema unchanged (flat rectangles + existing metadata).
7. Ensure variant selection precedes guide resolution.
8. Keep adapter untouched.
9. Add docs (runtime guide in M7b) and migration notes.

## 14) M7b test plan

Planned tests:

- API/type visibility for new guide types.
- Basic guide horizontal/vertical placement.
- One-axis relation: `left = target.right + offset` with parent-local top/height.
- Multiple targets across axes (one per axis).
- Missing target (`GuideTargetNotFound`).
- Self-reference (`GuideSelfReference`).
- Reference cycle (`GuideReferenceCycle`).
- Target exists but unresolved (`GuideTargetUnresolved`).
- Invalid edge axis mapping (`GuideInvalidEdgeForAxis`).
- Too many refs per axis (`GuideTooManyReferencesPerAxis`).
- Stack direct-child rejection.
- Grid direct-child rejection.
- Responsive variant selecting guide frame.
- Node-level offset applies after guide solve.
- Negative offsets allowed.
- Rect outside parent allowed.
- Resolved tree/flatten metadata preserved.
- `lerpResolvedLayouts` compatibility.
- Input immutability.

## 15) Risks and mitigations

1. **Solver creep risk**
   - Mitigation: separate `GuideFrame`, strict axis rules, no size refs, bounded refs.

2. **One-parent model erosion**
   - Mitigation: explicit invariant in docs/errors: parent owns coordinates; reference is read-only geometry input.

3. **Hidden graph coupling**
   - Mitigation: explicit target ids, explicit errors, bounded dependency pass, no implicit nearest-node lookup.

4. **Cycles/unresolvable chains**
   - Mitigation: dedicated cycle detection and error codes distinct from parent cycles.

5. **Clipping/layer confusion**
   - Mitigation: docs clarify layers affect paint order only; guide does not portal or alter clipping.

6. **AnchorFrame complexity regression**
   - Mitigation: no anchor reference support in v1.

---

## Decision record

- **M7b recommendation:** `GuideFrame` only.
- **Deferred:** `AttachFrame` and named guides.
- **Invariant preserved:** one parent per node; references are read-only alignment inputs.


## M7b runtime status
GuideFrame runtime dependency resolution, validation, and errors are implemented in core resolver.
