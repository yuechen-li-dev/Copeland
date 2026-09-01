# Inferred record-shape dogfood

**Outcome:** A. The small implementation makes local data authoring materially
simpler while preserving Copeland's existing nominal and compile-time boundaries.

## TS-style sample

```ts
function identity<T>(value: T): T { return value; }

function main(): int {
    const point = { x: 1, y: 2 };
    const peer = identity({ x: 3, y: 4 });
    const moved = point with { x: peer.x + 37 };
    const nested = { point: moved, label: "ready" };
    return nested.point.x + nested.point.y;
}
```

The same program executes as `42` through generated C# and Node. The generated
JavaScript value stays a frozen null-prototype branded record; the C# value stays
a sealed carrier with get-only members.

## C# comparison

The closest C# intuition is a private immutable record-shaped carrier plus a
`with` expression, but without authored type-declaration ceremony or synthesized
record equality/hashing. A named Copeland `record` remains the analogue for a
public reusable nominal contract.

## Findings

- Obvious: `{ x: 1, y: 2 }` is closed immutable data; `.x` reads it; `with`
  replaces fields without mutation.
- Obvious: contextual `{ ... }` still constructs a named record.
- Required explanation: inferred identity is exact and source-ordered. Equal
  ordered shapes intern; reordered uncontextualized shapes do not.
- Required explanation: an already-created anonymous value does not implicitly
  convert to a same-shaped named record.
- Remaining ceremony: public APIs still need named records; record-shaped
  `type` aliases are compile-time/static requirements rather than runtime carriers.
- Surprising behavior avoided: no spread, prototype, mutation, optional-property,
  `undefined`, equality, hashing, or runtime-reflection semantics arrived with
  object-literal familiarity.
- `type` versus `interface` is clearer: `type` names a type/shape; `interface`
  declares fields required by a generic capability. Neither performs computation.
  `template` computes and `reflect` explicitly observes compiler semantics.

## Exact next recommendation

Run CTS-REC-M5 named-shape qualification against a real public function/template
corpus before allowing structural `type` aliases in runtime parameter/return
positions. The decision must pin exact versus width assignment, anonymous carrier
selection, cross-module identity, TSON eligibility, and reflection names. Keep
method interfaces, optional properties, and broader TypeScript type algebra out
of that milestone.

Oblivion Function Cards and Theory UX remain checkpointed and were not touched.
