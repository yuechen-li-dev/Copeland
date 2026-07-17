# Copeland TS pure classes (CTS-CLASS-M1)

**Status:** implemented and closed.

```text
class
= controlled immutable nominal record
+ pure constructor
+ associated functions
+ privacy/invariant boundary
```

## Source law

A class creates one atomic nominal type and one qualified value owner. Its fields are closed, explicitly typed, declaration ordered, and immutable. `public` is the default; `private` is available; `protected` is rejected. There is exactly one primary `constructor`; source construction is `Person(arguments)`, never `new Person(arguments)`.

The constructor returns a complete contextual literal of its own type, or `ok`/`err` for its own `Person ! E` Result type. Fields are checked for unknown, duplicate, missing, and mismatched entries. Initializer expressions retain authored evaluation order while the carrier slots remain declaration ordered. Constructor fallthrough, partial construction, and construction of another class are rejected.

Class functions are qualified associated functions with no implicit receiver. `Person.birthday(person)` and `const f: (person: Person) => Person = Person.birthday` use the existing callable law. Private functions and fields are accessible only from code lexically owned by that class. An instance call receives a focused `COPE-CLASS-0011` diagnostic. A class-associated function may be generic through the existing bounded closed-specialization machinery; generic classes remain excluded.

Only code owned by `Person` can contextually construct `Person` or use `with` on `Person`. It can read and preserve private fields. Callers can construct via the public constructor, call/reference public associated functions, and read public fields. A class can satisfy field-only interface requirements through public fields only; it remains exactly nominal for assignment and generic identity.

## Canonical compiler representation

`ClassTypeSymbol` is a provenance-bearing specialization of the existing nominal record symbol. Bound construction, access, update, Results, calls, callable references, arrays, records, and payload enums all reuse their existing representations. `MirRecordDefinition.IsClass` and field visibility are the only retained MIR provenance; there is no method dispatch, vtable, object table, receiver, inheritance, or reflection model.

Shared MIR validation treats class carriers as nominal records for construction/access/update, while rejecting class participation in malformed TSON plans and table cells. The C# backend emits a sealed complete get-only carrier with an internal complete constructor, public source-public properties, and non-public private slots. Constructor and associated bodies lower as deterministic static generated functions. The JavaScript backend uses the existing private symbol slots, type token, WeakSet provenance, null prototype, frozen carrier, and terminal validator path. It emits no JavaScript `class`, `new`, `this`, methods, or prototypes in either profile.

## Explicit boundaries

Classes are excluded from TSON roots, reachable TSON plans, `tsonAsset`, `tsonEncode`, record-table cells, nominal-union alternatives, equality, JSON, generic classes, nested/partial/ambient/decorated classes, `new`, `this`, `super`, `extends`, inheritance, accessors, setters, mutable fields, field initializers, instance methods, and hidden receivers. A public associated projection to an ordinary record remains the future data-boundary route.

## Evidence

Language fixtures cover public/private state, contextual construction, class-internal updates, callable references, fallible constructors, erased interface requirements, closed generic associated functions, aggregates, and rejected object-shaped syntax. Focused C# tests compile and invoke the generated class carrier; repeated Node tests execute Diagnostic and Symbolic output and prove private provenance rejects a frozen null-prototype counterfeit. The representative fixtures are under `tests/Copeland/Copeland.TS.Tests/Language/*/classes`.

CTS-CLASS-M1 consumes [CTS-CALL-M1](copeland-ts-complete-callable-semantics-cts-call-m1.md) for callable values and [CTS-TYPE-M3](copeland-ts-foundational-type-system-closeout-cts-type-m3.md) for aliases, field-only requirements, and generic specialization. The accepted [M0a design](../language/copeland-ts-pure-classes-design-cts-class-m0a.md) remains the semantic authority.
