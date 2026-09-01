# Copeland record shapes, types, interfaces, and templates

**Decision:** Outcome A, `SMALL_IMPLEMENTATION`.

This review adds ordinary inferred immutable object literals without reopening
Copeland's closed foundational type system. Named records remain nominal runtime
products. Existing structural `type` shapes remain erased compile-time shape
descriptions used by templates, reflection, and requirement checking. Existing
field-only interfaces remain erased generic requirements. Templates remain the
compile-time computation mechanism and `reflect` remains explicit semantic
observation.

## Current-state audit

| Area | Current compiler path | Finding |
| --- | --- | --- |
| Syntax | `ObjectLiteralExpressionSyntax`, `StructuralObjectTypeSyntax`, `WithExpressionSyntax` | One brace syntax already serves contextual record construction and template structural values. |
| Named records | `RecordTypeSymbol` / `RecordFieldSymbol` | Nominal, closed, declaration ordered, immutable, stable compilation-local IDs. |
| Binding | `BindObject`, `BindWith`, `IsAssignable` | Object literals previously required a named contextual record at runtime; `with` already preserved exact nominal type. |
| Structural types | `StructuralObjectTypeSymbol` | Finite erased compiler shapes already exist for static/template work. They are width-assignable requirements, not runtime carriers. |
| Aliases | `TypeAliasSymbol.CanonicalType` | Transparent and erased before MIR. Structural aliases are accepted in compile-time/template positions. |
| Interfaces | `InterfaceSymbol` / `RequirementSet` | Field-only, constraint-only, erased; records/classes/table rows satisfy by readable fields and may have extras. |
| Generics | bounded direct inference and closed specialization | Nominal types are atomic. Object literals were deferred because they had no independent nominal evidence. |
| Templates | `BoundTemplateStructuralObject`, template type/static parameters | Compile-time construction; type parameters range over supported compiler types and use the normal requirement relation. |
| Reflection | `fieldsOf`, `nameOf`, `enumCasesOf`, `callsOf` | Explicit compile-time observation. `fieldsOf` supports records and structural types; no runtime reflection. |
| MIR | `MirRecordDefinition`, construction, access, and with nodes | Already expresses the complete inferred-value behavior once a compiler-owned record identity exists. |
| C# | deterministic sealed class with get-only fields | No C# record equality, setters, reflection, or dictionaries. |
| JavaScript | private type/field symbols and frozen null-prototype values | No prototypes, public textual-property semantics, or mutation. |

The boundary searches found the deliberate bounded structural utilities
`Pick`, `Omit`, `Partial`, `Required`, and `Readonly` in static/template binding.
They are retained for compatibility but are not generalized into runtime mapped
types. `keyof`, conditional types, indexed access, and `infer` remain rejected.
Existing `System.Reflection` use belongs to explicit CLR interop/compiler tests;
the inferred-record path adds none.

## Final semantic model

### Object literal law

With an expected named record type, a literal constructs that type directly and
retains existing missing, extra, duplicate, field-type, authored-evaluation-order,
and declaration-order laws.

Without a contextual type, a literal binds each field expression normally and
creates an immutable inferred record-shaped value. Nested literals recursively
infer nested shapes. Field order is source order. Fields cannot be added,
removed, deleted, or mutated.

### Anonymous identity law

Inferred records use an exact ordered structural key within one compilation:

```text
ordered field name + canonical field type, in source order
-> one interned compiler-owned record identity
```

Two independently inferred literals with the same ordered fields and canonical
types share that anonymous identity and assign normally. Reordered fields form a
different shape. A named record with the same fields remains a different nominal
type. Contextual construction is the explicit bridge: a literal expected as
`Point` constructs `Point`; an already-created anonymous value does not convert
to `Point`.

The interned type has no source name. Its stable semantic identity is derived
from the ordered shape, while its private backend carrier name is a full SHA-256
rendering. This avoids encounter-order ABI and prevents same-shape carrier
explosion without creating width-subtyping for runtime values.

### `with` law

`source with { replacements }` produces the exact same type as `source`, evaluates
the source once and replacements once in authored order, and replaces only known
fields. Empty updates, duplicates, unknown fields, additions, removals, and wrong
types are diagnostics. This law is identical for named and inferred records.

### Named `record`

`record` adds a stable authored name, nominal identity, reusable annotation,
public/schema suitability, TSON identity where supported, and a reflection name.
It does not add immutability; inferred record-shaped values are already immutable.

### `type`

`type` transparently names a compile-time type. Primitive, nominal, array,
Result, callable, and existing structural shape aliases canonicalize before MIR.
A record-shaped alias such as `type PointShape = { x: int; y: int; };` is an
erased structural requirement, not a separately constructible runtime nominal
record. It is width-assignable today: a record with at least the required fields
and compatible exact field types satisfies it. Optional/read-only projection
metadata stays in the static/template subsystem.

This milestone deliberately does not make structural aliases legal runtime
storage carriers. Public runtime parameters and returns should use named records.

### `interface`

An interface is an erased field capability used by generic and template
constraints. Satisfaction is implicit and structural at compile time: named and
inferred records, pure classes through public fields, and table rows satisfy the
required readable fields; extra fields are allowed. There is no `implements`,
runtime interface value, method dispatch, conversion wrapper, brand, or emitted
C#/JavaScript interface.

Field-only interfaces are retained as the established compatibility law. For
ordinary data, use an inferred value or named record. Use an interface when a
generic computation needs a capability. A later method-capability milestone may
extend this role; this milestone does not change interface grammar.

### Templates and reflection

Template `type T` ranges over the compiler's supported compile-time types,
including named records and structural type aliases. Constraints use the same
field-requirement relation as generic constraints; there is no template-only
assignability algebra.

`reflect fieldsOf<T>()` observes named records and structural type shapes in
declaration/source order. Through a template type parameter it observes the
closed source-spellable supplied type. `reflect nameOf<T>()` returns an
authored/canonical type name. Anonymous inferred values cannot currently be
named in template type syntax, so neither query accepts them and compiler-local
carrier names are never exposed. Interfaces are
requirements, not reflection targets; their fields are not mixed into record
metadata. `enumCasesOf` and `callsOf` remain category-specific.

## Assignability and capability matrix

| Question | Anonymous record shape | Named record | Record-shaped `type` | Interface |
| --- | --- | --- | --- | --- |
| Assignment/storage | Exact interned ordered identity | Exact nominal identity | Not an independent runtime carrier | Not a value type |
| Literal construction | Uncontextualized literal | Contextual literal | No direct runtime carrier | Not constructible |
| Parameter passing | Via inferred generic type or same interned shape | Via named annotation | Compile-time/static requirement positions only | Constraint only |
| Generic constraint | Satisfies field requirements | Satisfies field requirements | May describe a field requirement | Declares the requirement |
| Template constraint | Closed type may satisfy requirements when supplied | Supported | Supported erased shape | Declares the requirement |
| `fieldsOf<T>` | No current source route | Yes | Yes | No |
| `nameOf<T>` | No carrier-name exposure | Authored name | Alias/canonical display | No value/type reflection role |
| Runtime identity | Private compiler-owned record identity | Named nominal record identity | None | None |
| Extra fields for requirement satisfaction | Allowed | Allowed | Width requirement | Allowed by requirement law |

## Canonical authoring

```ts
const point = { x: 1, y: 2 };
const moved = point with { x: 3 };

record PublicPoint { x: int; y: int; }
const publicPoint: PublicPoint = { x: 1, y: 2 };

type PointRequirement = { x: int; y: int; };
interface HasX { x: int; }
```

Use inferred records for local closed data, named records for reusable/public
nominal data, `type` to name compile-time types/shapes, interfaces for generic
capabilities, templates for compile-time computation, and `reflect` for explicit
semantic observation.

## Explicitly rejected TypeScript behavior

- mutable properties, `delete`, dynamic insertion, prototypes, and dynamic lookup
- object spread as an update mechanism
- optional properties or `undefined` as absence
- width-subtyping between runtime anonymous record values
- implicit conversion from an existing anonymous value to a same-shaped named record
- `keyof`, conditional types, indexed access, `infer`, template-literal types,
  distributive types, and a general mapped-type runtime model
- runtime structural dispatch or reflection

The existing bounded static projections are retained, not expanded.

## Implementation and migration

The parser and MIR schemas did not change. The binder now:

1. permits omitted variable annotations for object literals and `with` values;
2. binds uncontextualized literals into interned exact ordered record shapes;
3. lets a previously context-free object literal provide generic inference
   evidence only when no other argument has closed the relevant parameter; and
4. adds the compiler-owned record definition to the normal record list.

Both backends reuse their existing record construction/access/update paths.
Existing contextual named-record behavior is unchanged. One stale generic
inference fixture now expects the more precise nominal mismatch: the generic call
successfully infers an anonymous record, which still cannot become `Point`.

No syntax migration is required. Code that previously failed because an object
literal lacked context now compiles with immutable inferred-record semantics.

## Recommendation

The exact next language milestone should be **CTS-REC-M5: named shape
qualification**: decide, with real public-API and template dogfood, whether an
erased record-shaped `type` should be usable as a runtime-facing contextual
annotation by selecting an anonymous carrier, or should remain compile-time-only.
Do not add it incidentally; it needs explicit exact-vs-width, reflection-name,
TSON, and cross-module identity qualification.

Oblivion Function Cards and Theory UX remain checkpointed and untouched.
