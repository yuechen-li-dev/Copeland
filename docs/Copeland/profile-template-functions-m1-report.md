# COPELAND-PROFILE-TEMPLATE-FUNCTIONS-M1 report

## Outcome

**Outcome A — Profile integrates with ordinary Copeland functions and typed
template specialization.**

```text
ordinary typed Profile function
  -> payload-enum ProfileOperation value
  -> template specialization returning ProfileOperation[]
  -> existing ProfileOperation IR
  -> geometric SSA
  -> canonical contours / SVG / M5 MSDF
```

No Profile parser, IR, macro engine, interpreter, renderer, or Boolean kernel
was added. Profile booleans and features are ordinary compile-time functions.
Templates specialize typed Profile values; they do not emit syntax.

## Existing compiler audit

| Concern | Existing support | M1 need | Change |
| --- | --- | --- | --- |
| template syntax | `template<type T, static value: Type> Name: Result` | unchanged | none |
| value parameters | typed defaults and named `instantiate` arguments | numeric specialization | reused |
| type parameters | real types plus normalized requirements | no numeric abuse | reused |
| return typing | normal `TypeSymbol`; structural values | nominal semantic arrays | contextual array/record binding |
| evaluation | structural plan and ordinary static evaluator | call pure ordinary functions | joined existing paths |
| arrays | immutable, ordered | `ProfileOperation[]`, record arrays | public typed result and element diagnostics |
| iteration | bounded `static for`; bounded loops in static functions | no tooth explosion | reused |
| nested calls | identity, cache, recursion/depth/count limits | `GearTeeth -> ToothFeature` | reused |
| ordinary calls | rejected in template plans | static-safe typed calls | `BoundTemplateOrdinaryExpression` |
| provenance | instantiation chain | operation correlation | template, args, span, index |
| backend staging | templates rejected from runtime MIR | erase Profile values | unchanged |

The generic blocker was the missing bridge from a bound template plan to the
already-qualified ordinary static evaluator. M1 adds that bridge generically.
Non-Profile tests cover `int[]`, named record arrays, payload-enum arrays,
mixed-element rejection, and unsafe-call rejection.

## Profile audit and final type law

M0 already owns immutable `ProfileShapeSpec`, `ProfileOperation`, named input
and output states, stable feature IDs, source spans, geometric SSA,
`RepeatRadialProfileOperation`, canonical contours, SVG, and the direct M5 path.
Its TSX adapter recognized operation-shaped calls, but templates could not
supply its ordered operations.

M1 defines ordinary records, `ProfileShape` and `ProfileOperation` payload
enums, and ordinary functions named `Add`, `Subtract`, `Hole`, `Tab`, `Notch`,
`RepeatRadial`, `Translate`, `Rotate`, `Scale`, and `Mirror`. Argument bags are
normal contextually checked records. The Profile host is the sole semantic
boundary that decodes immutable enum values into existing Profile IR; it does
not evaluate function names or reparse generated content.

`ProfileOperation[]` is a real ordered array. Template arrays compose with
direct operations, and the host threads the immutable current state through
generated operations in order. Existing Profile validation remains authority
for counts, dimensions, containment, states, and feature IDs.

## Canonical syntax

```ts
import { ProfileOperation, RepeatRadial } from "./Profile";

template<
    static count: int,
    static toothFraction: number,
    static toothDepth: number
> ToothFeature: ProfileOperation {
    return RepeatRadial({
        id: "GearTeeth",
        as: "WithTeeth",
        count,
        toothDepth,
        toothFraction,
        rotation: 90.0
    });
}

template<
    static count: int,
    static toothFraction: number,
    static toothDepth: number
> GearTeeth: ProfileOperation[] {
    return [instantiate ToothFeature<
        count: count,
        toothFraction: toothFraction,
        toothDepth: toothDepth
    >];
}
```

```tsx
export default (
    <Profile name="Gear" baseState="Base" base={Circle({ radius: 32 })}>
        {instantiate GearTeeth<count: 12, toothFraction: 0.52, toothDepth: 8.0>}
        {Hole({ as: "Hollow", id: "CenterHole", radius: 12 })}
        {Yield(Hollow)}
    </Profile>
);
```

The second reusable proof is `MountHole`, which calls ordinary `Hole` and
returns one semantic operation through an ordinary `CenterHole(radius):
ProfileOperation` helper. Functions remain reusable typed computation;
templates add static specialization. Type parameters remain only for types.

## Semantics, identity, and parity

`GearTeeth -> ToothFeature -> RepeatRadial` retains
`RepeatRadialProfileOperation`; it does not become rectangles or an anonymous
contour. The existing authored feature ID stays stable. Template provenance
records name, ordered arguments, instantiation span, and generated index without
changing semantic or contour hashes.

At count 12, manual and template Gear have exact Profile IR, contour, and SVG
parity. M5 consumes the same canonical `VectorShape`. Counts 8, 12, and 16
change one static argument and produce distinct deterministic template,
Profile IR, contour, SVG, and MSDF hashes in
`artifacts/copeland-profile-template-functions-m1/manifest.json`.

## Diagnostics and negative behavior

Normal binding diagnoses wrong record fields and result types. Template array
binding diagnoses mixed elements. Static evaluation rejects unsafe calls and
retains bounded recursion and resource diagnostics. The Profile host rejects
nonliteral specialization arguments and type arguments abused for numeric
values. Existing Profile validation diagnoses nonpositive/over-limit counts,
negative or non-finite dimensions, state chains, and feature failures.

No strings, source tokens, AST fragments, TSX fragments, or generated Profile
source cross specialization. The source library and template are ordinary input
modules; the returned typed value is decoded directly. There is no syntax
injection, output reparse, macro system, mutable builder, runtime Profile
allocation, or renderer change.

## Backends and regressions

Template declarations remain compile-time-only and do not enter runtime MIR,
so neither C# nor JavaScript emits compiler-only Profile types. M0 Profile
sources are unchanged. Flow TSX retains its semantic lowering. M5 consumes
unchanged contours through `ProfileVectorIconCompiler`; Machina/Aurelian native
realization is downstream and unchanged.

## Firmament and future authoring

Firmament feature tools could use pure semantic functions where application
topology selection is unnecessary. Value specialization and typed operation
arrays could improve reuse while Firmament remains owner of physical and
topological truth. This is documentation only; Aetheris was not modified.

The model can scale to `PelicanWing`, `BicycleWheel`, `Gear`, or `Badge`
helpers returning an operation, operation array, or Profile. That does not
justify raw SVG generation or an art DSL in M1.

## Evidence and next milestone

Focused tests are `TemplateTypedFunctionM1Tests` and
`ProfileTemplateFunctionsM1Tests`. `tools/Copeland.Profile.M1Evidence` writes
three SVG fixtures and the manifest.

Final validation:

- focused Profile/template/Flow/static lanes: 89 passed;
- `Copeland.TS.slnx`: 1,653 passed (1,208 compiler, 252 C#, 193 JS);
- `Machina.UI.slnx`: 738 passed;
- `Aurelian.slnx`: 650 passed;
- `JointTaskForce.slnx`: 3,358 passed;
- M5 native vector proof: 8 icons, 18 semantic uses, zero validation errors;
- M2 native text, M3 native Machina presentation, and M4 analytic SDF tools:
  Outcome A with zero Vulkan validation errors where reported;
- `git diff --check`: clean, with only the repository's existing CRLF warning.

The exact next milestone is **COPELAND-PROFILE-FUNCTION-AUTHORING-M2**: bind
non-template Profile TSX operation expressions through the same ordinary
Profile function contract, removing the remaining direct-call parser
duplication without changing Profile IR, syntax, or geometry.
