# Copeland TS immutable nominal records closeout (CTS-REC-M3)

**Status:** implemented and closed. CTS-REC-M0a through M3 are the ordinary immutable-record authority.

## Ratified source law

`record Point { x: number; y: number; }` declares one nominal product type with a closed, declaration-ordered set of explicitly typed fields. Every field is required, has no default, and is intrinsically immutable; `readonly` is neither required nor accepted. Duplicate declarations or fields and direct or indirect record-containment cycles are rejected. Equal shapes do not imply compatible types.

A contextual literal such as `const point: Point = { y: second(), x: first() };` requires one unambiguous expected nominal record type. It does not infer an anonymous structural object. Each declared field is supplied exactly once; missing, extra, duplicate, or wrongly typed initializers are rejected. Initializers evaluate once in authored left-to-right order. Canonical storage, record definitions, and constructor arguments remain in declaration order; canonicalization never reorders evaluation.

`point.x` resolves to one stable record and field identity. The receiver evaluates once. Only declared fields are accessible, and runtime textual property lookup is not source semantics. Field assignment and equivalent mutation routes are rejected.

`source with { y: second(), x: first() }` evaluates the source once, then each replacement once in authored order. It retains unspecified fields from the original source and constructs a new value of exactly the same nominal type. It never mutates the source or exposes a sequentially updated intermediate. Unknown, duplicate, empty, or wrongly typed replacement sets are rejected.

| Operation | Valid? | Meaning |
| --- | ---: | --- |
| `const p: Point = {...}` | Yes | Immutable binding to an immutable record |
| `let p: Point = {...}` | Yes | Reassignable binding containing immutable records |
| `p = p with {...}` for `let p` | Yes | Replace the binding with a new record |
| `p = p with {...}` for `const p` | No | Attempt to reassign a constant binding |
| `p.x = value` | No | Attempt to mutate a record field |

## Ratified MIR law

Canonical Cope MIR carries stable compilation-local `MirRecordTypeId` and `MirRecordFieldId` values. Definitions and field lists preserve declaration order. Construction and update lists separately preserve authored evaluation order. Field types remain exact, access names a resolved record/field identity, and `with` names a same-type source/result identity. All record references in fields, function signatures, locals, Results, arrays, and payload-enum fields require complete definitions.

Shared `MirValidator` validation is authoritative before backend emission. The closeout matrix covers nonexistent definitions and fields; duplicate record/field IDs; duplicate prohibited names; incomplete, extra, duplicate, and mistyped construction; mismatched access identity; wrong-type update sources/results; empty, duplicate, unknown, and mistyped replacements; record references nested in Results and payload-enum definitions; and unsupported cycles. C# returns an empty artifact and JavaScript returns no artifact when shared validation fails. There is intentionally no `.cope` parser or general schema framework.

## Exactly-once, order, and control flow

Focused frontend, backend, runtime, and parity tests establish:

- declaration order can differ from authored initializer/replacement order without changing evaluation order;
- nested construction and update do not duplicate inner evaluation;
- earlier arguments execute before later statementful record arguments, and earlier record arguments execute before later arguments;
- receivers, update sources, initializers, and replacements are captured once;
- conditional, payload-enum match, Result match, propagation, unwrap, and typed handler branches execute only when selected;
- logical `&&` and `||` preserve short-circuiting when the right operand has statementful record/Result lowering;
- unchanged update fields come from the original source, nested updates allocate new values, and the source remains observably unchanged;
- ordinary Result propagation and typed handler flow remain private structured control, not host exceptions.

Representative repeated parity results remain `1132` for argument/initializer ordering and `42` for the full record/Result/enum/handler vertical. The JavaScript update trace remains `source,second,first`. The M3 combined branch/order/access/update parity case returns `4651` identically and repeatedly in generated C# and Node.

## Representation boundary

| Language law | MIR | C# | JavaScript |
| --- | --- | --- | --- |
| Nominal identity | Stable record ID | Distinct sealed class | Private per-type Symbol |
| Closed ordered fields | Stable ordered field IDs | Fixed get-only members | Fixed Symbol properties |
| Complete construction | Validated initializer set | Complete constructor | Private complete builder |
| Authored evaluation order | Authored initializer list | Ordered temporaries | Ordered staging |
| Field access | Resolved field ID | Direct member access | Brand check plus Symbol read |
| Immutable update | Same-type update node | New class instance | New frozen value |
| No mutation | No mutation node | No setters | Frozen non-writable properties |
| Equality deferred | Unsupported operation | No source-visible equality | No source-visible identity comparison |

Each C# nominal record is one deterministic ordinary sealed class with complete internal construction and stable field-ID-derived get-only members. It has no setters, generated C# `record`/`record struct`, synthesized equality, hashing, cloning, deconstruction, reflection, `dynamic`, or dictionary storage. Public accessibility needed by generated proof signatures, CLR reference identity, member spelling, and layout are backend-private and do not define Copeland law.

Each JavaScript nominal record has a private per-type Symbol, private per-field Symbols, null prototype, fixed non-writable/non-configurable properties, complete construction before publication, and `Object.freeze`. Brand checks protect field access and update from another same-shaped record, ordinary or frozen objects, null-prototype impostors, payload enums, Results, and typed-flow records. This is compiler-owned nominal enforcement, not a cryptographic boundary against hostile reflective host code; public JavaScript interop remains undefined.

The record's shape and slots are immutable. Nested Copeland records are independently immutable. Contained values retain their own type's mutability law. No arbitrary transitive deep-freeze claim is made; host objects, mutable collections, and interop need separate policy.

## Equality boundary

Record `==` and `!=` both report `COPE-REC-0016`. C# reference equality and JavaScript identity are not exposed; private tokens are not user-comparable; no structural equality, hashing, or ordering is generated. The invalid filesystem equality fixture remains contractual. Any future decision must separately settle binary64 NaN and signed zero, nested records, Results, payload enums, future tables/collections, and recursive values.

## Diagnostics

| Code | Contract |
| --- | --- |
| `COPE-REC-0001` | invalid record declaration/member/modifier or placement |
| `COPE-REC-0002` | duplicate record declaration |
| `COPE-REC-0003` | duplicate declared field |
| `COPE-REC-0004` | unsupported recursive record containment |
| `COPE-REC-0005` | missing unambiguous expected record type |
| `COPE-REC-0006` | incomplete construction |
| `COPE-REC-0007` | unknown initializer or replacement field |
| `COPE-REC-0008` | duplicate initializer or replacement |
| `COPE-REC-0009` | initializer type mismatch |
| `COPE-REC-0010` | invalid or unknown field access |
| `COPE-REC-0011` | immutable field mutation |
| `COPE-REC-0012` | non-record `with` source |
| `COPE-REC-0013` | empty `with` |
| `COPE-REC-0014` | replacement type mismatch |
| `COPE-REC-0015` | nominal record type mismatch |
| `COPE-REC-0016` | unsupported record equality |

Diagnostics retain source names only for user-facing context; stable IDs remain semantic authority. Spans identify the responsible declaration, field, initializer, access, replacement, or operator. Binding reassignment continues to use the general constant-binding diagnostic, while field mutation uses `COPE-REC-0011`. No generated C# or JavaScript name appears in a source diagnostic. Valid records have no `COPE-CS-REC-0001` or `COPE-JS-REC-0001` production path. Malformed MIR is reported as backend invalid-MIR diagnostics (`COPE-CS-0002`/`COPE-JS-0002`), never as a source diagnostic.

## Fixtures, CLI, helpers, and artifacts

Ordinary filesystem fixtures visibly cover declaration/construction, access, nesting/context, distinct same-shaped nominal records, `let` rebinding through `with`, Result/payload-enum composition, and every requested invalid family. They stay within the ordinary xUnit fixture harness and contain no manifests or test DSL.

The CLI emits MIR, C#, and JavaScript for accepted record source. Generated C# compiles and executes through Roslyn; JavaScript executes through Node. Rejected source exits nonzero before writing. Backend rejection produces no partial artifact; tests distinguish an unchanged pre-existing sentinel from newly written output rather than treating stale bytes as a successful emission.

Record helpers are demand-driven. Programs without records emit none. Record-only programs do not pull in Result, enum, typed-flow, unit, or unwrap helpers. Composed programs emit each required family once in stable program order. Repeated compilation is byte-identical.

No existing corpus artifact changed in M3. Representative retained SHA-256 values are:

| Artifact | SHA-256 |
| --- | --- |
| `record-basic.g.js` | `AA91167AF8D33B45731748BF5D0861FBCE4EF7D195E96E2ADFFB7C77F62EB8A0` |
| `record-order-with.g.js` | `EC92548B37415D888B02ACB6C9D163096DD2D46FF66C23767E5BE0E43DA56060` |
| `record-result-enum.g.js` | `DDACF318CB2777D5A4E5A138B8875F3AB3752F8AD93D6C64DD185EF55B56BB24` |
| `record-try-except.g.js` | `859A7CD39986AC6D3410943A529AAA0222E320240FFE96D36A2E2883DC733F7D` |
| `basic.g.cs` | `7E7EB61D7F4607F55578E929D950F56865C7F7EC278BB7F28C90D4256102E2DC` |
| `initializer-order.g.cs` | `A567AB3CA0CC5ACBE69DD2D3BE0988FF1B46827A6DA83C11210F80C9F7E5E1E4` |
| `nested.g.cs` | `DC7D989C0CF26C0B093D55DA023B3A94CFFD4A7FCD582E084D412585FE7EF8BC` |
| `with.g.cs` | `602D89CC68A6D49B117709EEE9C5F44D54C56F28C22EE91D1D91D9E883291ADB` |
| `result.g.cs` | `57377DDCB6607B43A31A7E8064E7851443D18DCD56E39C3E4AEB3168074B957C` |
| `payload-enum.g.cs` | `81D569833B169BB2CF95C394FB8266B551026473A33853A3E37A6CD1E7E4FEF3` |

## M3 defect closeout and deferred boundaries

The audit found two general expression-lowering defects while applying adversarial record contexts. JavaScript validation rejected canonical logical `&&`/`||` MIR and therefore could not preserve a statementful right operand's selected-branch staging. C# statementful payload-enum matching inferred a concrete case class for a direct constructor scrutinee, making other exhaustive cases illegal C# patterns. M3 now accepts typed logical MIR in JavaScript and stages a statementful right operand inside the selected branch; C# now stores statementful match scrutinees in the declared enum base type. Both fixes have non-record or direct-constructor regression evidence and preserve all corpus artifacts.

No record-specific workaround, source-name identity, shape compatibility, equality surface, mutation path, eager unrelated helper, or obsolete valid-record backend rejection remains. Equality, hashing, ordering, patterns/destructuring, methods, classes/interfaces/inheritance, defaults, mutable records, spread, dictionaries, JSON, public interop/ABI, generic or recursive records, reflection, and compiler-wide IR changes remain excluded.

The ordinary immutable-record ladder is closed. The recommended next separately approved product ladder is **CTS-TABLE-M0a**, limited to design and audit; this milestone does not define or implement record tables.
