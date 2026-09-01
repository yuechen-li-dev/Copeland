# Copeland TS parser-level language burn-in

## Outcome

**Outcome B — the core language is sound, but repeated composition and codegen
pressure appears.** Five implementation-derived programs reached their intended
real paths. Four emitted deterministic MIR, JavaScript, C#, and stable Node
output; the template/reflection program evaluated deterministically to 15
compile-time artifacts. The pass found and fixed two async record backend
correctness bugs and one class diagnostic bug. The remaining material pressure
is bounded: pure function calls inside FLOW updates, constrained template type
parameter forwarding, and table runtime size.

This verdict is based on `SyntaxNodes.cs`, `Parser.cs`, `Binder.cs`, bound nodes,
static/template evaluation, `MirLowerer.cs`, MIR validation, and both backends.
Documentation was consulted only after the source inventory was built.

## Implementation-derived feature inventory

Status terms are `WORKS`, `RESTRICTED`, `COMPILE_TIME`, `BACKEND_SPECIFIC`, and
`UNSUPPORTED`. “C#” and “JS” describe the ordinary runtime backend unless the
row says compile-time.

| Feature | Parser / syntax node | Binder / semantic form | MIR | JS | C# | Evidence and restriction |
|---|---|---|---|---|---|---|
| Primitive literals and typed equality | `LiteralExpressionSyntax`, unary/binary nodes | primitive bound expressions | WORKS | WORKS | WORKS | null is profile-rejected; `===` is reserved |
| `const` / `let` | `VariableDeclarationStatementSyntax` | immutable/mutable local symbols | WORKS | WORKS | WORKS | `var` parses but profile-rejects |
| Functions and returns | `FunctionDeclarationSyntax` | function symbols/effects | WORKS | WORKS | WORKS | explicit types at boundaries |
| Named generic functions | type parameters, generic call/reference nodes | closed specialization | specialized functions | WORKS | WORKS | bounded inference; no runtime generics |
| Callable values and explicit capture | callable/arrow/capture syntax | callable construction and invoke | WORKS | WORKS | WORKS | exact signatures; no equality |
| Immutable arrays | array type/literal/index syntax | array expressions and checked access | WORKS | WORKS | WORKS | homogeneous, bounds checked |
| `MutableArray<T>` | generic calls, index assignment | dedicated mutable-array forms | WORKS | WORKS | WORKS | explicit `freeze()` snapshot |
| Named records | record declarations/object literals | nominal `RecordTypeSymbol` | record definitions | WORKS | WORKS | frozen/get-only carriers |
| Inferred records | uncontextualized object literals | interned exact ordered record types | reused record MIR | WORKS | WORKS | local/generic use; no structural runtime boundary |
| `with` | `WithExpressionSyntax` | record/table update forms | WORKS | WORKS | WORKS | class updates only inside owning class |
| Pure classes | class/constructor/associated-function nodes | `ClassTypeSymbol` over nominal record semantics | `MirRecordDefinition.IsClass` | WORKS | WORKS | no JS class/prototype/receiver/inheritance |
| Type aliases | `TypeAliasDeclarationSyntax` | transparent alias or structural shape | erased | N/A | N/A | runtime storage must resolve to concrete nominal types |
| Interfaces | `InterfaceDeclarationSyntax` | erased field requirements | erased | N/A | N/A | constraints only; public class fields participate |
| Payload enums | enum/case/payload nodes | nominal enum and case symbols | enum definitions/match | WORKS | WORKS | zero/payload cases and nested payloads work |
| Nominal record unions | union type syntax in alias declaration | canonical nominal union | enum-shaped | WORKS | WORKS | alternatives must be direct records; classes excluded |
| `match` / `switch` expression | match/pattern/arm nodes | enum/result match | WORKS | WORKS | WORKS | exhaustive nominal cases |
| `T ! E`, `ok`, `err` | `ResultTypeSyntax`, calls | result types/construction | WORKS | WORKS | WORKS | distinct validated result identities |
| `?` propagation | `PropagateExpressionSyntax` | explicit propagation target | WORKS | WORKS | WORKS | error type must match target |
| `!` unwrap | `UnwrapExpressionSyntax` | explicit unwrap | WORKS | WORKS | WORKS | failure follows backend invariant/failure contract |
| `try` / `except` value | dedicated try/value-block nodes | lexical result handler | WORKS | WORKS | WORKS | expression form, not host exceptions |
| `if`, if-expression | statement/expression nodes | typed control flow | WORKS | WORKS | WORKS | branch types must agree |
| `while`, `for`, `for...of` | loop nodes | bound loops/iterables | WORKS | WORKS | WORKS | break/continue supported |
| Synchronous generators | function/yield nodes | generator function | WORKS | WORKS | WORKS | `yield`, `yield return`, `yield*`; no async generators |
| Async / await | async function/type/await nodes | async effects and result propagation | suspension automaton | WORKS after fix | WORKS after fix | compiler-owned async computation, not naked Promise/Task semantics |
| `batch` | `BatchExpressionSyntax` | restricted pure batch body | WORKS | serial bounded loop | parallel C# | no nesting, host effects, or mutable capture |
| Pipelines | binary pipeline spelling | ordinary nested calls | ordinary calls | WORKS | WORKS | no pipeline runtime node |
| Record tables | table/column/derived nodes | nominal table, row, column types | columnar table MIR | WORKS | WORKS | bounds return `TableBoundsError`; arrays rejected in runtime cells |
| Table access | index/member syntax | row/column access | WORKS | WORKS | WORKS | no row-object conversion |
| Table query forms | ordinary calls over table/column types | where/select/aggregate forms | WORKS | WORKS | WORKS | bounded typed query surface |
| FLOW | flow/board/event/state/transition nodes | dedicated flow graph | flow definitions | WORKS | WORKS | transition calls broadly effect-rejected in M1 |
| `template` / `instantiate` | template declaration/instantiation nodes | bound structural plans | no runtime MIR | COMPILE_TIME | COMPILE_TIME | preview/materialize path only |
| `static` expression/control | static expression/if/for/match nodes | bounded static values | erased | COMPILE_TIME | COMPILE_TIME | finite approved operations only |
| `reflect nameOf` | reflect/generic-call nodes | semantic type metadata | erased | COMPILE_TIME | COMPILE_TIME | deterministic |
| `reflect fieldsOf` | same | typed field metadata | erased | COMPILE_TIME | COMPILE_TIME | records/structural types |
| `reflect enumCasesOf` | same | typed enum-case metadata | erased | COMPILE_TIME | COMPILE_TIME | payload enums only |
| `reflect callsOf` | same | direct compiler-owned call sites | erased | COMPILE_TIME | COMPILE_TIME | direct calls only, bounded metadata |
| Imports / exports | import token declaration/export nodes | project/package identities | module graph | RESTRICTED | RESTRICTED | local source imports require project compiler; package contracts bounded |
| CLR `using` / construction / calls | using/new/member nodes | resolved metadata members | CLR MIR | backend reject | WORKS | explicit CLR backend boundary |
| npm calls/components | import/call/TS-XML syntax | declared npm contracts | npm MIR | WORKS | sidecar path | flat transport shapes |
| JavaScript host contracts | imports/calls | declared host contract | host-call MIR | WORKS | backend specific | no ambient dynamic JS lookup |
| TSON encode/assets | calls and declaration profiles | typed encoding plans | TSON plans | WORKS | WORKS | nominal values; classes excluded |
| Option/optional fields/chaining/coalesce | optional field, `?.`, `??` nodes | compiler-owned `Option<T>` | enum/match lowering | WORKS | WORKS | no `undefined`/truthiness semantics |
| Layout/stream/component declarations | dedicated declaration families | layout/presentation semantic models | specialized MIR/data | RESTRICTED | RESTRICTED | product/compiler profile, not ordinary object semantics |
| TS-XML | element/fragment/attribute nodes | bounded React/component forms | React MIR | WORKS | profile-specific | explicit React profile only |
| Inline C# | `CSharpBlockStatementSyntax` | captured C# body | backend-specific | reject | WORKS | forbidden in generator/batch bodies |

## Explicit unsupported or recovery-only surface

The parser contains nodes for some familiar spellings so it can recover or route
them precisely. That does not make them Copeland runtime features.

| Surface | Classification | Current implementation truth |
|---|---|---|
| JavaScript/TypeScript mutable classes, prototypes, instance methods | UNSUPPORTED | only pure immutable nominal classes exist |
| `new` for a Copeland class | PARSES_BUT_RESTRICTED | COPE-CLASS-0013; pure call construction is required |
| `this`, `super`, `extends`, inheritance | PARSES_BUT_RESTRICTED / RECOVERY | focused class rejection |
| mutable object properties, `delete`, object spread | UNSUPPORTED | no semantic/runtime object model |
| dynamic property access | PARSES_BUT_RESTRICTED | indexing is arrays/tables/columns, not arbitrary objects |
| `undefined` and JS truthiness | UNSUPPORTED | Option and typed boolean semantics replace them |
| broad TS unions/intersections | PARSES_BUT_RESTRICTED | nominal record unions and constraint intersections only |
| arbitrary mapped/conditional types, `keyof`, `infer` | UNSUPPORTED | bounded `Pick`/`Omit`/`Partial`/`Required`/`Readonly` projections only |
| runtime reflection | UNSUPPORTED | four explicit compile-time semantic queries only |
| generic classes | UNSUPPORTED | associated functions may be generic |
| async generators | PARSES_BUT_RESTRICTED | COPE-GEN-0002 |
| record/class equality and hashing | PARSES_BUT_RESTRICTED | intentionally no identity/equality law |
| structural runtime dispatch/conversion | UNSUPPORTED | interfaces and structural aliases erase |

## Burn-in programs and coverage

| Feature | Application | Tables | Flow | Metaprogramming | Async/Batch/Generator |
|---|:---:|:---:|:---:|:---:|:---:|
| inferred/named records, nested `with` | ✓ |  |  |  | ✓ |
| pure class, privacy, constructor, associated generic | ✓ |  |  |  | ✓ |
| `type` / interface constraints | ✓ |  |  | ✓ |  |
| payload enum / match | ✓ | ✓ | ✓ | ✓ |  |
| Result / propagation / unwrap | ✓ | ✓ | ✓ |  | ✓ |
| arrays / loops / evaluation order | ✓ |  |  |  | ✓ |
| record table / row / columns |  | ✓ |  |  |  |
| FLOW states/events/guards/terminal paths |  |  | ✓ |  |  |
| template/static/all four reflection queries |  |  |  | ✓ |  |
| template memoization/reuse |  |  |  | ✓ |  |
| async / await / generator / batch |  |  |  |  | ✓ |
| C# emission | ✓ | ✓ | ✓ | N/A | ✓ |
| Node execution | ✓ | ✓ | ✓ | N/A | ✓ |

The corpus has 605 source LOC: Application 212, Tables 106, Flow 116,
Metaprogramming 90, and Async/Batch/Generator 81. The first file is deliberately
ordinary application code; the others concentrate feature families without
turning into synthetic parser input.

## Program findings

### Application

The ordinary code is readable once the three value categories are understood:
inferred records for closed locals, records for data/API boundaries, and classes
for invariant/privacy boundaries. `Parcel` composes with Result construction,
record containment, interface constraints, immutable `with`, and a generic
associated function. The class syntax pays for itself when private
`normalizedCode` and fallible construction are real; a record remains less
ceremonial for `Position` and `OrderLine`.

CTS-REC-M4 behaved correctly. Four unique anonymous ordered shapes produced
four carriers. Repeated `{ x, y }` and the nested position shape reused carriers;
`{ y, x }` deliberately did not. Nested `with` retained source evaluation order.
The explicit structural return probe failed, so `LocalPoint` remains the correct
named Result boundary.

### Tables

The source stays columnar. Row views do not materialize general row objects, and
column accesses keep `TableBoundsError` explicit. Records, enums, and Results
work as cells. Array-valued runtime columns were rejected by deep-immutability
qualification even though the separate TSON table asset surface supports nested
arrays. The major backend pressure is size: the small program's validators,
table singleton, result/enum machinery, and access helpers produce a 17.847 JS
size ratio.

### Flow

The 8-state, 7-event flow emitted a direct state-switch session with explicit
guard branches, immutable board replacement, terminal state, revision, and
reentrancy checks. It is verbose but traceable, not spaghetti. The material
friction is authoring: even `nextSequence(board.sequence)` is rejected by the
FLOW-M1 update effect rule, forcing repeated inline arithmetic.

### Metaprogramming

All four reflection queries are deterministic and compile-time-only. The source
produces 15 artifacts and no runtime JavaScript. `fieldsOf` and `enumCasesOf`
preserve declaration semantics; `callsOf` reports only the three direct call
sites of `CompileService`, including the duplicate. Static control and direct
template instantiation compose. Forwarding `T extends Named` into another
template with the same constraint does not, which is the clearest template
composition gap.

Two identical `Label<value: "same">` instantiations feed distinct artifact paths;
their bytes agree and exercise the evaluator's existing memoization path.

### Async, batch, generator, and class composition

Generator delegation, array collection, batch mapping, Result checking,
async/await propagation, an inferred record, and pure class construction after
`await` compose in one program. That exact combination exposed both backend
bugs fixed by this pass. Node now returns `ok/84`, and the generator yields
`0,1,2,3,4`. The JS batch realization is a deterministic loop; C# retains its
parallel batch path.

## Class assessment against JavaScript and traditional TypeScript

Copeland's pure class works better than a JS/TS class when the problem is a
closed immutable value with an invariant boundary. `Parcel(...)` returns a
validated complete value, private storage remains compiler-private, `with`
cannot be used by outside code to bypass the invariant, and associated
operations have explicit data flow. Generated JS uses a frozen null-prototype
carrier, private Symbols, a WeakSet provenance check, and ordinary associated
functions. It emits no `class`, prototype, `this`, hidden receiver, mutable field,
or constructor side-effect sequence.

That strength is also its limit. It is not better for identity-bearing mutable
objects, inheritance, polymorphic dispatch, or framework classes because those
semantics are deliberately absent. A TS developer may initially expect `new`
and instance methods; the corrected COPE-CLASS-0013 diagnostic now explains the
actual pure-call model. A C# developer will recognize a sealed immutable carrier
plus static operations, but must learn that private `with` authority replaces
record-wide public copying.

## Diagnostics and recovery

The three semantic composition probes each produced one focused diagnostic after the
class fix: COPE-REC-0005 for structural runtime construction, COPE-TABLE-0009
for array table cells, and COPE-FLOW-0024 for the helper call. `new Person`
initially produced the misleading COPE-CLR-0001; it now produces the intended
COPE-CLASS-0013. Parser recovery is weaker: deleting the comma in
`{ x: 1, y: 2 }` produces 11 diagnostics. That is material recovery friction,
but fixing it was deliberately deferred to avoid turning this pass into parser
work.

## Language friction and footguns

- Ordered structural identity is a language footgun, but the corpus did not make
  it materially painful. Reordering creates a distinct type/carrier.
- `!` is visually compact but means explicit Result unwrap, not TypeScript's
  non-null assertion. Diagnostics and generated Result validation make it
  repairable.
- Table access returns Result; forgetting the table-specific error type at a
  propagation boundary is easy and correctly diagnosed.
- Pure class syntax resembles TS more than its semantics do. The construct is a
  nominal value/invariant boundary, not an object model.
- Template constraints carry evidence into a body but currently lose it when a
  type parameter is forwarded to another constrained template.

## LLM and developer legibility

Semantic boundaries are mostly visible: `record`, `class`, `record table`,
`flow`, `template`, `static`, and `reflect` each announce a different owner.
Inferred records remove local ceremony without hiding public identity. Generated
Diagnostic-profile JS is useful for inspecting validators and ordering, but is
too large for line-by-line debugging of tables. Focused diagnostic IDs were
sufficient for autonomous repair except for the corrected `new Person` case.

TS familiarity is helpful for literals, functions, arrays, generics, async,
imports, and class declaration shape, but potentially misleading for objects,
classes, `!`, and optional values. C# familiarity helps with records, payload
enums, Results, explicit numeric conversion, and sealed class intuition; FLOW,
templates, and columnar tables remain intentionally Copeland-specific.

## Verdicts

- **Parser:** `PARSER_HAS_RECOVERY_FRICTION`. The surface is coherent, but it
  intentionally parses/recover-routes some TS-looking constructs that later
  reject; the `new` route needed one diagnostic repair.
- **Binder/type system:** `BINDER_HAS_LOCAL_GAPS`. Ordinary records, pure classes,
  interfaces, Results, enums, arrays, and generics compose well. FLOW pure-call
  qualification and template constraint forwarding are local repeated gaps.
- **MIR:** `MIR_SUFFICIENT_WITH_LOCAL_PRESSURE`. No MIR redesign was needed. The
  async bugs were backend catalog/profile omissions over valid existing MIR.
- **JavaScript backend:** `JS_BACKEND_VERBOSE_BUT_SOUND` after the async fix.
  Tables are materially verbose; all valid programs execute deterministically.
- **Compilation performance:** `COMPILE_PERF_FINE`. Warm in-process totals are
  small for this corpus; the manifest retains per-stage coarse measurements.

## Top evidence-backed next actions

1. Qualify effect-classified pure Copeland calls inside FLOW transition updates.
2. Preserve normalized interface evidence through constrained template type
   parameter forwarding.
3. Measure shared table/runtime helper factoring against the 17.847 size ratio
   before proposing optimization passes.

## Validation

All requested gates passed: Copeland.TS (1,541 tests), Copeland (1,724),
JointTaskForce, Oblivion (237), Machina.UI (674), Machina.UI.Slow (308), the
no-restore Machina build with zero warnings/errors, and Aurelian no-build (657).
Canonical Presenter playback passed 14/14 with zero failures and skips.
`git diff --check` passed. Boundary searches found 35 existing reflection uses
owned by CLR interoperability, CLI/materialization, Roslyn projection, and
backend helpers; this change adds none.

The full gate record is
`artifacts/cts-burn-in/cts-burn-in-validation.json`.

No parser rewrite, MIR redesign, speculative type machinery, mutable object
model, optimizer framework, Oblivion UI, Function Card, Theory UX, or native
graph work was performed. Oblivion Function/Theory work remains checkpointed.

Machine-readable evidence is in
`artifacts/cts-burn-in/cts-burn-in-manifest.json` and
`artifacts/cts-burn-in/cts-burn-in-findings.json`.
