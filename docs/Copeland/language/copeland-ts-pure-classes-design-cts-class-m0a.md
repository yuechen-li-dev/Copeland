# Copeland TS pure classes design (CTS-CLASS-M0a)

**Status:** accepted architecture and repository-audit milestone; no production implementation is authorized by this document. CTS-CLASS-M1 is the one proposed implementation milestone.

## Executive decision

A Copeland class is a declaration that defines one immutable nominal value, controls the construction and immutable updating of that value, and groups associated named functions. It deliberately has no JavaScript object model:

```text
class
-> class-owned immutable nominal record
-> pure primary constructor function
-> associated named functions
-> ordinary record/function MIR
```

There is no `new`, `this`, prototype, instance method, inheritance, virtual dispatch, partial initialization, object identity, or callable/reference equality. A class is not declaration merging and is not a CLR inheritance hierarchy.

## Audited current state

The source of truth is the migration audit at [CTS-CLASS-M0a audit](../../migrations/cts-class-m0a-class-and-record-construction-audit.md). In brief, the current compiler has implemented nominal records, functions/callables, Results, arrays, payload enums, field-only interfaces, named closed generic functions, C# and JavaScript record realizations, TSON, and tables. It has no class token, syntax node, symbol, bound node, MIR node, visibility model, class backend, or class test fixture.

The current [language profile](copeland-ts-language-profile.md) therefore owns implemented records and this intended, unimplemented class direction. The completed callable and foundational type work are inputs, not alternate class designs: [CTS-CALL-M1](../architecture/copeland-ts-complete-callable-semantics-cts-call-m1.md) supplies callable values and explicit capture; [CTS-TYPE-M3](../architecture/copeland-ts-foundational-type-system-closeout-cts-type-m3.md) supplies erased field-only requirements and closed generic functions.

## Source shape and namespaces

The initial recognizable spelling is:

```ts
class Person {
    public name: string;
    private normalizedName: string;
    age: number;

    private normalize(name: string): string {
        return name;
    }

    constructor(name: string, age: number) {
        return {
            name,
            age,
            normalizedName: Person.normalize(name),
        };
    }

    birthday(person: Person): Person {
        return person with {
            age: person.age + 1,
        };
    }
}

const john = Person("John", 22);
const older = Person.birthday(john);
```

One declaration intrinsically creates two names:

```text
type namespace:  Person = immutable nominal class-owned record type
value namespace: Person(...) = primary constructor; Person.birthday(...) = associated function
```

`public` is the default, as in TypeScript. M1 accepts only `public` and `private`; `protected` is rejected because there is no inheritance. `readonly` is rejected as redundant: every class field is immutable. Fields are declaration-ordered, explicitly typed, and cannot have initializers. A field declaration is not executable initialization.

The class body permits exactly one `constructor` and zero or more associated functions. It has no nested classes, partial declarations, class constants, decorators, accessors, setters, overloads, async constructors, or generic class parameters. Additional named factories are public or private associated functions returning `Person` or `Person ! E`.

All class functions use method-shaped syntax with the `function` keyword omitted. `static` is rejected, rather than accepted as cosmetic noise: functions in a class are already associated. Every parameter and non-implicit return follows the existing function law. Associated generic functions use existing named generic-function specialization and must be closed before a callable reference can be made.

Class-local lookup is deliberately qualification-only. `Person.normalize(name)` is the canonical spelling even inside `Person`; an unqualified name resolves by existing lexical/global rules and never silently selects a member. This avoids a second member scope, shadowing surprises, and any implication of a receiver. `Person` is available in its own bodies as both its type and associated-value owner. Class fields are not names in function scope; they require an explicit value (`person.age`).

## Record versus class

| Property | Ordinary `record` | `class` value |
| --- | --- | --- |
| Construction | Open contextual record literal | Constructor or class-associated code only |
| Immutable update | Open same-type `with` | Associated code for that class only |
| Purpose | Transparent data | Invariant/privacy boundary plus operations |
| Associated operations | None | Qualified named associated functions |
| Nominality and fields | Existing nominal record identity | Existing nominal-record machinery with class provenance/access metadata |

Both remain complete immutable nominal product values. A class does not acquire a separate field algebra, anonymous conversion, prototype chain, default constructor, setter, or mutation phase.

## Construction, publication, and updates

The primary constructor is a pure associated function. A nonfallible constructor has an implicit expected/declared result `Person`; M1 permits an optional redundant `: Person` annotation only when it is exactly the owning type. A fallible constructor must spell exactly `: Person ! E`:

```ts
constructor(name: string, age: number): Person ! PersonError {
    if (age < 0) {
        return err(PersonError.InvalidAge(age));
    }

    return ok({ name, age, normalizedName: Person.normalize(name) });
}

const person = Person("John", 22)?;
```

The ordinary call has its ordinary inferred `Person ! PersonError` type; `?` uses the existing Result target law. Existing all-path return analysis applies. The nonfallible contextual literal is the direct class value; the fallible literal is the payload of `ok`. Every declared field must appear exactly once, in authored declaration order in the resulting carrier; missing, extra, duplicate, and mistyped fields are rejected. Initializer expressions evaluate exactly once in written literal order before the complete carrier is published. Constructor overloads and multiple constructors are rejected.

Construction and update authority is lexical source authority, not a runtime capability:

* The primary constructor and associated functions of `Person` may contextually construct `Person` and use `with` on `Person` values.
* Outside code cannot contextually construct `Person` or use `with` on a `Person` value.
* An associated function of `A` gains no authority over `B` merely by receiving `B`.
* Public fields are readable everywhere; private fields and private associated functions are readable/callable only in their owning class-associated code.
* A public associated function may intentionally expose an updater; that is ordinary authored behavior, not hidden mutation.
* Captured callables retain CTS-CALL explicit lexical capture and do not carry class authority to a caller.

`new Person(...)` is rejected with a `COPE-CLASS-*` diagnostic saying construction is a pure call: `Person(...)`. `person.birthday()` is rejected as instance invocation, with `Person.birthday(person)` (or a future pipeline) suggested. A future pipeline may support `Person("John", 22) |> Person.birthday`; this milestone neither implements nor redesigns it.

## Type-system composition

A class is atomic nominal type identity in the existing type algebra. It may contain primitives, records, other class values, payload enums, Results, arrays, callables, and table/row/column views where the existing storage law admits them. The initial containment rule is conservative: reject direct and transitive class/record cycles through records, arrays, Results, payload enums, and other classes with an iterative deterministic graph walk. Existing record cycles are the precedent.

Class values may occur in record fields, enum payloads, Result values, and arrays. The current nominal-union restriction to direct ordinary record alternatives is not widened: class-owned records remain excluded until an explicit union extension approves them. Constructors return only their owner type or owner `! E`; associated factories may return other existing types.

Classes satisfy erased field-only interfaces implicitly through accessible public fields only. Private fields never satisfy a public requirement. Public callable fields use exact existing callable requirements; associated functions do not satisfy an interface field unless a callable is explicitly stored in a public field. Generic functions accept a class as an exact atomic nominal type; generic associated functions reuse closed-specialization identity. Generic classes are deferred.

Associated named functions can be callable values; for example:

```ts
const celebrate: (person: Person) => Person = Person.birthday;
```

M1 does not make constructors callable values. This prevents constructor authority and fallibility from becoming an unexamined generic callable conversion; a later proposal can add it with an explicit type law.

## TSON, tables, equality, and serialization

Class construction owns invariants, so a class is initially outside the TSON schema/value/asset/encoding algebra. `tsonAsset` cannot instantiate one; `tsonEncode` rejects a class root and a reachable class field; table cells reject class values; JSON has no class representation. No decoder, reflection path, or automatic projection may bypass the constructor. The future safe exchange direction is explicit authored projection:

```text
class value -> associated function -> public record DTO -> TSON
```

Class equality is rejected. Backend reference identity is representation detail and callable/reference equality remains unsupported.

## Lowering and realization

The canonical M1 frontend lowering is class provenance on an existing nominal record declaration plus ordinary constructor/associated functions and frontend-only access-control metadata. Prefer no class-specific MIR: existing `MirRecordDefinition`, construction, field access, `with`, `MirFunction`, direct calls, function references, callable construction, and Results express runtime behavior. A narrowly scoped Bound-only class construction/update provenance may survive until access checks have produced ordinary record Bound nodes; it is justified only if it preserves diagnostics and authority, not as a new object model.

Stable identities cover the class type, ordered field identities, constructor, associated functions, and generic associated specializations. Reuse deterministic stable identity and collision-safe naming rules.

For C#, emit an ordinary sealed carrier, not source inheritance semantics:

```text
public sealed class Person
  public get-only Name/Age
  internal complete constructor
  public static Person Create(...)
  public static Person Birthday(Person person)
  private static string Normalize(...)
```

No public partial constructor, setters, reflection, `dynamic`, or source-level receiver is allowed. A fallible constructor uses the existing Result carrier as `CopeResult<Person, PersonError>` (subject to existing backend naming). C# consumers receive this comprehensible projection while source calls remain `Person(...)` and `Person.birthday(person)`.

For JavaScript, reuse record machinery: private class type token, private field `Symbol`s, provenance `WeakSet`, frozen null-prototype carrier, compiler-owned constructor function, and ordinary generated associated functions. Do not emit JavaScript `class`, `new`, `this`, prototype members, enumerable field names, or a public constructor object. Build every slot before freeze/publication; private slots remain closed and public access is compiler-known. Diagnostic and Symbolic emission share this semantic path, and demand emission remains deterministic.

## Diagnostics, limits, and exclusions

M1 reserves `COPE-CLASS-*` for malformed declarations/members; duplicate fields/members; type-name collision; missing/extra/duplicate constructor fields; constructor mismatch or incomplete return; external literal/`with`; private access; instance syntax; `new`; `this`/`super`; inheritance; mutable/initialized fields; unsupported class feature; recursive containment; class TSON/table/equality use; and resource limits. Existing exact record/function/access diagnostics should be reused where they state the law accurately.

Recommended bounded limits, frontend enforced before MIR, are: 64 fields/class, 64 associated functions/class, 32 private helpers/class, 256 classes/compilation, containment depth 16, existing 16 closed specializations/generic definition and 128/compilation, and four displayed members in diagnostics. Dependency/cycle work uses deterministic iterative worklists.

M1 explicitly excludes `new`, `this`, `super`, `extends`, inheritance, virtual/override/abstract semantics, instance methods, prototype members, mutable fields, setters/accessors, initializers, partial values, overloads, generic/nested/partial classes, decorators, reflection construction, automatic TSON/JSON, equality, finalizers, operator overloads, and async constructors. Familiar unsupported syntax should receive a class diagnostic where parser support can do so without broad grammar work.

## CTS-CLASS-M1 scope and evidence

One M1 implements the complete accepted feature through source, binding, existing record/function MIR, C# and both JavaScript profiles, diagnostics, corpus, and parity. It must test construction/fallibility, public/private fields, associated update/helper, callable references, explicit capture, composition with records/enums/Results/arrays/interfaces/generics, C# API shape, and JavaScript counterfeit rejection. Invalid evidence covers external construction/update, field completeness, privacy, `new`, `this`, instance calls, inheritance, mutable/initialized fields, returns, cycles, equality, TSON/tables, and generic/nested/partial forms.

## Unresolved owner decisions

Owners must approve the numeric limits and diagnostic truncation; whether optional redundant `: Person` is worth accepting; whether constructor callables should ever be admitted; exact generated C# visibility for private source fields; and the later union alternative, class-to-DTO/TSON, and CLR importer policies. None blocks M0a or changes the recommended M1 semantic core.
