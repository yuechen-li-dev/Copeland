# Copeland TS TSON arrays design (CTS-TSON-ARRAY-M0a)

**Status:** design/audit accepted and implemented by [CTS-TSON-ARRAY-M0b](../architecture/copeland-ts-tson-arrays-and-assets-cts-tson-array-m0b.md). Historical implementation findings below describe the pre-M0b baseline.

## Executive decision

TSON arrays are ordinary Copeland TypeScript arrays used in TSON's restricted data profile. The one new semantic value is `TsonArray`; the one corresponding schema form is structural `TsonArraySchema(elementType)` (represented in the existing type-reference algebra as an array element reference when implemented). There is no `$array(...)` syntax, no parallel collection model, and no dynamic/JSON fallback.

`TsonArray` is immutable, finite, ordered, homogeneous under exactly one TSON element schema, defensively copied, and without reference/alias identity. It may contain nested supported TSON values. The authoritative type evidence is the schema's explicit element schema; the value must retain that schema (or an immutable reference to it) as well as validated elements. Keeping both is the smallest robust design: the schema is authoritative, while the value can validate and print without rediscovering context. This is necessary for `[]`; neither `never[]`, `unknown[]`, nor `any[]` is inferred.

The first implementation keeps the existing root law: one nominal record or payload enum root. Arrays are initially reachable only through a record field or enum payload. Runtime encoding is deferred to ARRAY-M1. The exact next milestone is [CTS-TSON-ARRAY-M0b](#exact-cts-tson-array-m0b-recommendation).

## Repository audit

The audit was made against `debd5f7f78f5e3f32929b1c2727af4000b1b5001` on `main`; `origin/main` resolved to the same revision and the worktree was clean before documentation edits.

| Area | Current implementation evidence | Finding for arrays |
| --- | --- | --- |
| Array syntax/type syntax | [`SyntaxNodes.cs`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs) defines `ArrayLiteralExpressionSyntax` and `ArrayTypeSyntax`; [`Parser.cs`](../../../src/Copeland/Copeland.TS/Syntax/Parser.cs) parses postfix `T[]` and `[...]`. | Production syntax already exists; array trailing commas are accepted because a final comma is retained before `]`. No hole, spread, or computed-element syntax node exists. |
| Binding/types | [`Types.cs`](../../../src/Copeland/Copeland.TS/Semantics/Types.cs) defines structural `ArrayTypeSymbol`; [`Binder.cs`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs) `BindArray` contextually binds elements, rejects non-equivalent inferred elements, and reports `COPE-TYPE-0010` for an uncontextualized empty literal. | Arrays are homogeneous and nested array types work recursively. Contextual record fields, payloads, variables, and nested expected array elements supply empty-array evidence. |
| Indexing/mutation/aliasing | `IndexExpressionSyntax` exists, but `Binder.BindIndex` accepts only table/column receivers; `BindAssignment` treats indexed assignment as immutable table/row assignment. There is no bound or MIR array-index/array-assignment node. | Copeland source currently has no array indexing, `length`, mutation, or bounds law. `let` can rebind an array-valued binding and multiple bindings can carry the same runtime carrier, but source cannot mutate its elements. |
| Bound/MIR/text | `BoundArrayExpression` is in [`BoundNodes.cs`](../../../src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs); [`MirLowerer.cs`](../../../src/Copeland/Copeland.TS/Lowering/MirLowerer.cs) lowers it to `MirArrayExpression` and `MirArrayType` in [`MirNodes.cs`](../../../src/Copeland/Copeland.TS.Mir/MirNodes.cs); [`MirTextWriter.cs`](../../../src/Copeland/Copeland.TS.Mir/MirTextWriter.cs) renders it as `[...]`; [`MirValidator.cs`](../../../src/Copeland/Copeland.TS.Mir/MirValidator.cs) recursively validates elements/types. | Asset loading can reuse existing bound/MIR array construction; no TSON-only array MIR is warranted. |
| C# realization | [`CSharpBackend.cs`](../../../src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs) emits `new T[] { ... }`; `MapType` recursively maps `MirArrayType` to `T[]`. [`M0hRuntimeTests.cs`](../../../tests/Copeland/Copeland.TS.Backend.CSharp.Tests/Runtime/M0hRuntimeTests.cs) executes primitive and enum array returns. | CLR arrays are physically mutable and aliasable, even though current Copeland source does not expose mutation. |
| JavaScript realization | [`JavaScriptBackend.cs`](../../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs) currently reports `MirArrayExpression` and `MirArrayType` as unsupported. | There is no production JavaScript ordinary-array realization yet. This is a real gap, not evidence to invent a new TSON carrier. ARRAY-M0b must add the minimal normal-array realization if `tsonAsset` arrays are to compile to JavaScript. |
| Fixtures/corpus | [`Language/Valid/arrays/homogeneous-array.cl-valid.ts`](../../../tests/Copeland/Copeland.TS.Tests/Language/Valid/arrays/homogeneous-array.cl-valid.ts), `TestData/Corpus/m0-csharp-valid/array_literal.*`, and `m1-enum-match-*/enum_array_type.*` are current array evidence. `Tson/Invalid/array.obj.ts` proves core TSON currently rejects arrays. | Existing evidence is primitive/enum and C#-weighted; there is no current nested-array, sparse-array, mutation, or JavaScript-array acceptance test. |
| Records/enums/Results/tables | Contextual record construction in `Binder.BindObject`, payload enum binding, `ResultTypeSymbol`, and table-specific `MirTable*` forms all compose with `ArrayTypeSymbol` in general type traversal. The table constant validator explicitly rejects mutable arrays. | Record and enum arrays are syntactically/type-theoretically available. Results and tables are not currently eligible TSON values; table arrays remain a separate future contract. |
| TSON semantic model | [`TsonValues.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonValues.cs) has only `TsonBoolean`, `TsonNumber`, `TsonString`, `TsonObject`, `TsonRecord`, and `TsonEnum`; its collection helpers defensively copy collections. [`TsonSchema.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonSchema.cs) has the matching six-type catalog. | `TsonArray` and one structural array schema are the smallest semantic addition. Existing core is intentionally six variants, not an implemented array feature. |
| TSON reader/printer/limits | [`TsonDocumentReader.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonDocumentReader.cs) uses `SyntaxTree.Parse`, restricts executable forms, carries expected types into values, rejects schema cycles, checks lexical/semantic depth and node/string limits. [`TsonCanonicalPrinter.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonCanonicalPrinter.cs) owns four-space/LF canonical text. | Add array schema/value projection and traversal to these existing owners; no lexer/parser extraction. Add an array-length limit alongside the existing aggregate node/depth/string/source limits. |
| Assets | `Binder.BindTsonAsset`, `ValidateTsonSchemaType`, `TsonTypeMatches`, and `TryLowerTsonValue` in [`Binder.cs`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs) require one nominal root and lower records/enums to ordinary bound constructions. | Add expected-array schema matching and `TsonArray -> BoundArrayExpression` recursively. Root arrays remain rejected. |
| Encoding plans/backends | `MirTsonEncodingPlan` and its six value plans are in [`MirNodes.cs`](../../../src/Copeland/Copeland.TS.Mir/MirNodes.cs); plan validation and canonical static text are in [`MirValidator.cs`](../../../src/Copeland/Copeland.TS.Mir/MirValidator.cs) and [`MirTsonCanonicalText.cs`](../../../src/Copeland/Copeland.TS.Mir/MirTsonCanonicalText.cs). C#/JavaScript writers dispatch only primitive/record/enum plans. | ARRAY-M1 can add `MirTsonArrayPlan(elementPlan)` to this shared plan, not a general array MIR. Both writers must validate it before emission. |
| Dependency/topology | Existing TSON types are colocated in `Copeland.TS.Tson`; backend-neutral plans are in `Copeland.TS.Mir`; the repository provides `tools/Validate-CopelandTsTopology.ps1` and `tools/Validate-DependencyBoundaries.ps1`. | Keep this ownership. No package/project/syntax extraction is justified. |

Historical M0b/M1b/M2b/M2c documents accurately record that arrays were excluded from the core; those statements are historical implementation boundaries, not a competing array design.

## Current Copeland array semantics

`T[]` is a structural, recursively composable type and `[e0, e1, ...]` is a left-to-right ordinary expression. With an expected `T[]`, every element is bound against `T`; without one, the first element establishes the type and every remaining element must be equivalent. Thus `[1, "two", true]` is rejected. `[]` requires a contextual array type and is rejected otherwise. Nested literals obtain context recursively, so `const rows: number[][] = [[], [1, 2]]` is typeable.

The parser permits ordinary comments/trivia accepted by the production lexer and permits a trailing comma. A comma without an element is not a semantic hole: parser recovery may produce diagnostics, and TSON must reject it. Spread syntax, elisions, computed elements, and an array-specific syntax are absent from the current syntax model. Ordinary Copeland literals can contain arbitrary supported expressions, but TSON's restriction pass will admit only closed data expressions.

Arrays have no source indexing, bounds, `length`, mutation, equality, or identity law. C# happens to use a mutable CLR array; JavaScript has no supported language-array lowering. That physical fact does not make arrays immutable source values, and it must not silently redefine normal Copeland arrays.

## TSON array value and schema law

```text
TsonValue
  └── TsonArray(elementSchema, elements)

TsonSchema
  └── TsonArraySchema(elementSchema)
```

`TsonArraySchema` is structural: two array schemas are the same when their element schemas are the same, independently of declaration site. Arrays receive no stable URI identity. Nominal record and enum elements keep their own catalog identities. `TsonArray` copies its input sequence into an immutable collection, rejects null internal entries, records element schema, and validates each element against that schema in declaration/index order. It represents a finite value tree, never a graph.

For a nonempty array, element validation is still required even though values may appear alike. For `[]`, `elementSchema` is mandatory and is the only authoritative evidence. Schema declarations, record fields, enum payloads, root annotations if roots are ever enabled, and nested array schema positions retain that evidence in canonical `.tson`.

### Homogeneity and nominality

All elements must match one schema:

- Primitive arrays accept only the matching primitive.
- Record arrays accept one exact nominal record identity. Same-shaped, differently nominal records cannot mix.
- Enum arrays accept all cases of one exact nominal enum identity; cases from different enums cannot mix.
- Nested arrays recurse structurally through their element schema.
- `$object` arrays are document-only and deferred unless a future exact structural object schema is supplied; they cannot be projected to a runtime object carrier.

This deliberately represents typed JSON-style arrays rather than all heterogeneous JSON. It does not introduce unions, `unknown`, `any`, or a dynamic fallback.

### Empty arrays and roots

An empty TSON array is accepted only with element evidence from a contextual field/payload, an explicit variable/root annotation if roots are later approved, or a nested array schema. An uncontextualized `const values = [];` is rejected. The existing canonical envelope therefore remains self-describing.

Initial roots stay nominal:

```text
record/enum root
  -> array field/payload
  -> typed elements
```

Explicitly typed array roots are accepted but deferred. They would enlarge the existing `tsonAsset`/`tsonEncode` root identity law and canonical envelope surface without helping the first nested-array use case.

## Immutability, aliasing, and cycles

| Concern | Copeland runtime array | `TsonArray` |
| --- | --- | --- |
| Carrier | Existing C# output is a normal mutable `T[]`; JavaScript normal arrays are not yet supported. | Compiler-host immutable semantic data. |
| Source mutation | No current array element assignment/indexing surface. | Impossible after construction. |
| Aliasing | Ordinary references could share a physical carrier once a backend exposes one. | No observable alias or reference identity. |
| Encoding meaning | Future encoder observes the carrier it receives. | Canonical document snapshot. |

For asset ingestion, `TsonArray` is a compile-time immutable snapshot and lowers to a fresh ordinary array construction; no compiler-host object is exposed at runtime. For ARRAY-M1 runtime encoding, `tsonEncode` evaluates the array expression once, observes its length once, then reads each index once in ascending order. A mutation made before that call is reflected; the current single-threaded expression model has no interleaving mutation during the traversal. No transaction, clone, locking, or snapshot-isolation law is invented. If a future host/concurrency boundary permits re-entrancy, that boundary must define it explicitly.

Recursive schemas remain compile-time errors, including record/enum paths passing through arrays. Repeated equal subarrays are separate data values; reference sharing is not preserved. Existing eligible source types cannot construct an array self-cycle through current array operations, so no runtime cycle detector is justified. A future runtime type capable of cycles is ineligible until a bounded detection rule is designed.

## Syntax and canonical text

Authoring uses ordinary Copeland literals:

```ts
record Sample {
    names: string[];
    scores: number[];
}

const $value: Sample = {
    names: ["Ada", "Grace"],
    scores: [10, 20],
};
```

Canonical `.tson` uses the same grammar, four spaces, LF, deterministic element order, a trailing comma after every multiline element, and one final document LF. Empty arrays print as `[]`; nonempty arrays print multiline even when short, avoiding a second formatting policy:

```ts
const $value: Sample = $record.Sample({
    "names": [
        "Ada",
        "Grace",
    ],
    "scores": [
        $number("4024000000000000"),
        $number("4034000000000000"),
    ],
});
```

Nested arrays increase indentation by one level. ARRAY-M0b must make that exact spelling part of the reader/printer fixed point. It introduces neither `$array(...)` nor JSON spelling.

## Scope by element family

| Family | Decision | Reason |
| --- | --- | --- |
| Boolean, Number, String | Implement in ARRAY-M0b | Existing primitive TSON leaves and ordinary array typing. |
| Record, Enum | Implement in ARRAY-M0b | Existing nominal identity/catalog and bound/MIR constructors. |
| Nested Array | Implement in ARRAY-M0b | Structural schema recursion and existing recursive array type/MIR. |
| Structural object | Accepted but deferred | Document-only objects lack an exact runtime structural schema/carrier. |
| Result | Accepted but deferred | Results are control semantics, not current TSON data. |
| Table/row/column | Accepted but deferred | Needs the separately designed table TSON law. |
| Optional | Accepted but deferred | No current optional schema/value law. |
| Interfaces/type aliases | Accepted but deferred | They belong to future schema algebra, not array values. |

An eventual alias may name an array schema (`type Names = string[]`), and an eventual interface may declare an array-valued field. Neither adds a new array value form or belongs in this ladder.

## Limits and diagnostics

ARRAY-M0b should add `MaximumArrayLength`, defaulting to **100,000**, equal to the existing default `MaximumValueNodeCount`. This is a bounded per-container limit consistent with current TSON limits: every element also counts toward the aggregate 100,000 nodes, arrays contribute to nesting depth, strings retain the 262,144 UTF-16-code-unit limit, and canonical source/output remains bounded by existing 1,048,576-character/source and 1,048,576-byte encoding limits. The exact normal constructor/limit parameter order is implementation detail for M0b, but it must be positive and validator-covered.

Traversal is deterministic left-to-right. Compile-time overlength asset input is a TSON resource diagnostic; malformed/missing array schema or unsupported element type is a compiler diagnostic. ARRAY-M1 runtime encoding maps output exhaustion to `TsonEncodeError.OutputLimitExceeded`, invalid runtime string content in an array to `InvalidUnicode`, and applies existing M2b per-string/Unicode/output precedence for each element. Schema depth/cycle violations are compiler diagnostics, never runtime JSON fallbacks.

## Compile-time asset lowering

The future asset path is intentionally ordinary:

```text
TsonArray
  -> validate expected T[] schema/type
  -> BoundArrayExpression in index order
  -> MirArrayExpression / MirArrayType
  -> normal C# or JavaScript array realization
```

No TSON array MIR is needed for this operation; `MirTsonArrayPlan` is only for later encoding metadata. Empty arrays use the expected `ArrayTypeSymbol` rather than inference. Nominal record/enum elements validate identity before recursive lowering.

```ts
record Batch {
    names: string[];
    items: Item[];
}

enum Load {
    Ready(values: number[]),
}

const batch: Batch = tsonAsset("./batch.tson");
const load: Load = tsonAsset("./load.tson");
```

ARRAY-M0b must not retain a runtime parser, filesystem path, TSON value, or backend TSON dependency. C# already realizes this normal array MIR. JavaScript does not yet; the M0b implementation must add only the necessary direct literal/type realization and validation to make asset outputs parity-capable, not a TSON-specific carrier or dynamic collection API.

## Deferred runtime encoding design

ARRAY-M1 extends the shared encoding plan with:

```text
MirTsonValuePlan
  └── MirTsonArrayPlan(elementPlan)
```

The plan carries the recursive element plan, so empty runtime arrays are typed by plan/schema rather than contents. Roots remain nominal record/enum. Plan construction includes reachable array schemas structurally; it does not assign an array identity or definition. `MirTsonCanonicalText` renders `T[]`; `MirTextWriter`, plan lowering, shared validation, and both writers consume exactly that same plan.

C# emits direct indexed access over the established `T[]` carrier. JavaScript emits direct closed indexed access over the established array carrier; it never uses `for...in`, property discovery/enumeration, `JSON.stringify`, a host serializer, reflection, or a parser. Each backend evaluates the operand once, obtains length once, then reads and encodes each index once in ascending order. Existing output, Unicode, node, depth, and array-length checks apply.

Shared validation rejects a missing element plan, an unsupported/inconsistent element type, array-schema cycle, over-depth plan, structural-object/Result/table/optional element plan, deferred root array, and malformed nested plan before either backend emits. This extends the current `MirValidator` fail-before-artifact model rather than duplicating backend checks.

## JSON and table boundaries

TSON arrays may later map to JSON arrays, with a crucial qualification: only non-null JSON primitives/objects, unique object keys, and homogeneous arrays that satisfy an explicit TSON schema can map structurally. Heterogeneous JSON arrays require a future union/schema policy or are rejected. JSON `null` remains a compatibility-layer value, not a TSON array element. This ladder implements no JSON parsing, writing, or compatibility API.

Arrays are a prerequisite for future columnar table exchange, not table exchange itself:

```text
record table
  -> future TSON record/object containing typed column arrays
```

That later work still needs a TSON Result law for represented bounds/errors, table schema/value law, column identity/order, and canonical columnar lowering. It must not project table carriers as arbitrary arrays.

| Comparison | Boundary |
| --- | --- |
| Homogeneous TSON array vs. heterogeneous JSON | TSON has one validated schema; JSON has no such default. |
| Array schema vs. nominal record/enum schema | Array schemas are structural containers; records/enums retain stable nominal identities. |
| Asset lowering vs. runtime encoding | Asset lowers compiler-host immutable data to ordinary bound/MIR construction; encoding observes an ordinary runtime carrier through a closed plan. |
| Nested arrays vs. record tables | Nested arrays preserve tree order; tables require column/row schema and rectangular/identity laws. |

## Decision matrix

| Item | Decision |
| --- | --- |
| Homogeneous arrays | Implement in ARRAY-M0b |
| Heterogeneous arrays | Rejected |
| Empty contextual arrays | Implement in ARRAY-M0b |
| Uncontextualized empty arrays | Rejected |
| Primitive arrays | Implement in ARRAY-M0b |
| Record arrays | Implement in ARRAY-M0b |
| Enum arrays | Implement in ARRAY-M0b |
| Nested arrays | Implement in ARRAY-M0b |
| Structural-object arrays | Accepted but deferred |
| Result arrays | Accepted but deferred |
| Table arrays | Accepted but deferred |
| Optional arrays | Accepted but deferred |
| Root arrays | Accepted but deferred |
| Sparse arrays / holes | Rejected |
| Spreads | Rejected |
| Reference aliases | Rejected from `TsonArray`; ordinary runtime aliasing remains outside current source law |
| Cycles | Rejected at schema validation; no runtime graph format |
| JSON mapping | Accepted but deferred compatibility work |
| Runtime decoding | Accepted but deferred |

## Exact CTS-TSON-ARRAY-M0b recommendation

**CTS-TSON-ARRAY-M0b: add the compiler-host `TsonArray` and structural array schema; reuse existing production array syntax and bound/MIR array construction; implement only homogeneous Boolean/Number/String/Record/Enum/nested arrays beneath nominal record/enum roots; add canonical reader/printer and `.obj.ts`/`.tson` fixtures; implement `tsonAsset` recursive lowering; and add the minimal normal JavaScript array realization required for asset parity. Defer `tsonEncode` array plans/writers to ARRAY-M1.**

This is bounded and non-atomic with runtime encoding because the current plan, validator, canonical static text, both writers, and M2b error/limit proofs would all need coordinated runtime work, while assets can safely use the established bound/MIR construction now. It excludes JSON, tables, runtime decoding, Results-as-data, interfaces, aliases, optionality, new roots, and any parallel grammar.
