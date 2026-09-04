# Profile function authoring M2

Outcome A: Profile TSX now supplies structure, while ordinary typed Copeland
functions and values construct geometry. The Profile host recognizes the
`<Profile>` root and `Yield` state marker. It does not recognize geometry
function names.

## Special-case audit

| Previous special case | Why it existed | Ordinary replacement | M2 action |
| --- | --- | --- | --- |
| `ParseOperation` switched on `Hole`, `Tab`, `Notch`, transforms, and `RepeatRadial` | M0 preceded typed static function values | ordinary symbol lookup, call binding, and static evaluation | removed |
| `OptionReader` decoded operation object literals | direct calls bypassed record binding | nominal argument records and normal record diagnostics | removed |
| `ParseShape` switched on base-shape call names | base expressions bypassed ordinary calls | `ProfileShape` return values | removed |
| direct children had to be named call expressions | the TSX host consumed syntax, not values | any static-safe expression returning `ProfileOperation` or `ProfileOperation[]` | removed |
| templates had a separate result decoder | M1 introduced typed template values incrementally | the same centralized typed-value materializer used by ordinary values | narrowed |
| enum edge names were manually converted from syntax | M0 accepted `Top`/`Right` shorthand | ordinary `ProfileEdge` values; legacy shorthand is injected as typed constants | narrowed for compatibility |
| operation/shape enum-case decoding | semantic payloads must become the existing C# Profile IR | final compiler-host materialization boundary | retained and centralized |

The authoritative path is:

```text
ordinary expression/function/import
-> ordinary binder and nominal argument records
-> general bounded static evaluator
-> StaticEnumValue / StaticArrayValue
-> return-type-driven Profile materializer
-> existing immutable ProfileOperation SSA
```

Profile intrinsics are ordinary source declarations in the compiler-owned
`Profile.ts` module. Their signatures are the single binder authority. The
implementation uses payload-enum constructors, but that intrinsic
implementation is not surface syntax and is not consulted by TSX lowering.

## Authoring examples

Direct built-in:

```tsx
<Profile name="Gear" base={Circle({ radius: 32.0 })}>
    {Hole({ id: "Center", as: "Cut", radius: 12.0 })}
    {Yield(Cut)}
</Profile>
```

Ordinary helper and value:

```ts
function CenterHole(radius: number): ProfileOperation {
    return Hole({ id: "Center", as: "Cut", radius });
}

const Center: ProfileOperation = CenterHole(12.0);
```

Template specialization remains the M1 `instantiate GearTeeth<...>` contract;
its typed `ProfileOperation[]` result reaches the same materializer. Imported
helpers use normal project module resolution:

```ts
import { CenterHole } from "./ProfileTemplates";
```

## Composition and diagnostics

Local scalar values, operation arrays, ordinary helper functions returning
either accepted type, imported helpers, immutable record `with`, payload-enum
`match`, and ordinary `if` expressions all pass through the general static
evaluator. Object-literal unknown/missing/wrong fields are ordinary record
diagnostics. A geometry-looking function returning `int` is rejected by its
result type. Mixed arrays fail normal array binding before the Profile host.

Array spread is deferred: ordinary immutable array spread is not currently a
general Copeland expression feature, so M2 adds no Profile-only spread syntax.
Cross-file ordinary static-safe functions are qualified. Template
specialization retains M1 provenance; ordinary helper results retain the
Profile source site and stable authored feature ID. A richer general function
call provenance stack remains future compiler infrastructure, not a Profile
special case.

## Parity and erasure

M0 and M1 tests require exact Profile IR, contour, and SVG parity between manual
and template Gear authoring. The canonical M0 hashes remain unchanged:

| Fixture | Profile IR | Canonical contours |
| --- | --- | --- |
| Gear | `eff5141089e6c9e0e285bcd23df4c1e8623b05a2b2ed4cecb47bda4e4b88d03d` | `663176df3e459fba57e430ebad9ef3a56773d985305354c4ddbbf595d2f4e91e` |
| TabbedBadge | `90a199bdb73cf6b8bac2d6a45f0bea50e9e2d2cff3952acb8c087ccc1bdb552d` | `ee8fc45104a805044963a3175228205b70d79902fec15b4a210116304b4954c1` |
| Shield | `789448fe966265b6f9f74a9418fa1548e061bb1b7516494eb7bccfc07b8241d7` | `288ff0bf933c6da2ec0eb835f5450efbfa166ee2d42988862de82aa7432e08c1` |
| MultiHole | `74bccb01d6a77b7762a09a02b6a52f70ad2e141330fae0bda425fd96b2e12f43` | `e81aecd037e4cc13806141c9c92eaefc1df15ca62b2990f5ae80f762103803a2` |

Profile values are consumed during compilation. No React/JSX runtime,
Profile-operation runtime objects, geometry builder, or template runtime is
added to C# or JavaScript output. Geometry, contour, SVG, MSDF, and renderer
implementations are unchanged.

The LLM-facing edit surface is now ordinary semantic arguments and named
helpers. Gear tooth count, center-hole radius, badge edge/notch size, and
multi-hole membership can be edited without coordinate regeneration or raw
SVG. The same model scales to future `PelicanWing` or `BicycleWheel` helper
libraries without TSX registration.

A fresh-context API-only review selected exactly those edits: change the
`GearTeeth` count and `CenterHole` radius, change the badge `ProfileEdge` and
notch dimensions, and append mirrored `Hole` calls with distinct IDs. It
explicitly rejected raw SVG, JSX components, and geometry-internal edits.

Firmament should prefer pure semantic feature functions, nominal named-argument
records, helper modules, and templates returning operation arrays; no Aetheris
code was changed in this milestone.

The exact next milestone is `COPELAND-PROFILE-LLM-VECTOR-COMPOSITION-M0`:
qualify fresh-context semantic edits over reusable Profile helper libraries,
without adding syntax or changing geometry.
