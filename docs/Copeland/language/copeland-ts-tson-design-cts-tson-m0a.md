# Copeland TS native typed data: CTS-TSON-M0a design

**Status:** accepted documentation-only design and repository audit. No TSON production surface exists at this milestone.

> **M0b correction:** The recommendation in this historical design for a dedicated TSON parser, parallel canonical grammar, external-only schemas, and independent `Copeland.TS.Tson` parser project is superseded by [CTS-TSON-M0b](../architecture/copeland-ts-tson-shared-parser-and-semantic-model-cts-tson-m0b.md). TSON is a restricted semantic projection of ordinary Copeland syntax. M0b reuses `SyntaxTree.Parse`, embeds restricted record/enum declarations plus `$schema` and `$value` bindings, and emits only syntax accepted by that parser. The six-value algebra and JSON-after-TSON direction remain ratified.

> **M2b routing:** [CTS-TSON-M2b](../architecture/copeland-ts-runtime-tson-encoding-cts-tson-m2b.md) implements canonical runtime encoding for the nominal Boolean/Number/String/record/payload-enum subset without adding runtime parsing or a public runtime TSON value.

## Executive decision

TSON is one backend-neutral semantic data model with one canonical textual form. Its name expands to **TypeScript Object Notation** inside the Copeland project. The repository contains no earlier TSON contract, and this document does not claim that TSON is an external TypeScript standard.

The first value algebra is closed:

```text
TsonValue = Boolean | Number | String | Object | Record | Enum
```

Every TSON value is a finite immutable tree. A structural object is an exact ordered sequence of uniquely named fields and has no nominal identity. A record has a stable exchange identity plus the exact fields of that record schema in declaration order. An enum has a stable enum identity, an exact case identity, and its exact named payload fields in declaration order. These distinctions exist in the semantic nodes; they are not inferred from JavaScript shapes, JSON objects, CLR types, or transient compiler IDs.

Canonical TSON text is self-describing for nominal values. A separate, explicitly schema-directed decode operation may accept identity-eliding shorthand and must restore and validate nominal identity from the expected schema. There is no ambient or accidental hybrid. JSON is a later lossy lowering from TSON, never TSON's semantic foundation.

CTS-TSON-M0a changes documentation only. The bounded next milestone is specified in [CTS-TSON-M0b recommendation](#bounded-cts-tson-m0b-recommendation).

## Terminology and non-goals

| Term | Meaning in this design |
| --- | --- |
| TSON semantic value | One validated node in the closed `TsonValue` algebra. |
| canonical TSON text | The unique UTF-8 textual spelling selected for a semantic value. |
| nominal identity | A stable schema/source identity carried by record and enum nodes and canonical text. |
| expected schema | An explicit caller-supplied record or enum schema used by a distinct schema-directed decoding operation. |
| structural object | An identity-free exact ordered field value; not a JavaScript object. |
| canonical round-trip | Parse canonical text, validate it, print it, and parse it again without changing the semantic value or printed bytes. |

TSON is not Cope MIR, a `.cope` replacement, TSPack `*.xtest.tsx`, a TypeScript AST dump, JavaScript object-literal runtime semantics, JSON/JSON5/BSON/MessagePack, a host-serializer wrapper, `DocumentMir`, VD-MIR, a universal language IR, an arbitrary CLR-object serializer, a reflection-driven object graph, a target-pack/provider abstraction, a mutable DOM, or a speculative general compiler-infrastructure extraction. It is a bounded typed-data layer owned by Copeland TS.

## Repository evidence

The audit was performed from clean revision `27e2a3566ace092e9b56fc9a837dd89d17a5c2cc` on branch `main`. Evidence is classified below as implemented contract, historical/proof-era material, or proposed direction.

### Implemented source and frontend contracts

- [`SyntaxNodes.cs`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxNodes.cs) and [`Parser.cs`](../../../src/Copeland/Copeland.TS/Syntax/Parser.cs) contain `RecordDeclarationSyntax`, `EnumDeclarationSyntax`, `EnumCaseSyntax`, `ObjectLiteralExpressionSyntax`, `ObjectPropertySyntax`, `WithExpressionSyntax`, array syntax, and table syntax. The object parser admits identifier or string property tokens, but parser acceptance alone is not language approval.
- [`Binder.cs`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs) contextually binds `ObjectLiteralExpressionSyntax` only as `BoundRecordConstructionExpression`. It diagnoses an uncontextualized brace value with `COPE-REC-0005`, checks duplicate, unknown, missing, and mismatched fields, and reorders accepted initializers to record declaration order. Resolved reads and `with` become `BoundRecordFieldAccessExpression` and `BoundRecordWithExpression`; general dynamic members remain rejected.
- [`Types.cs`](../../../src/Copeland/Copeland.TS/Semantics/Types.cs) implements primitive `number`, `string`, and `boolean`; arrays; Results; nominal `EnumTypeSymbol`; nominal `RecordTypeSymbol`; and compiler-local `RecordTypeId`/`RecordFieldId`. `TypeFacts.AreEquivalent` makes separate record symbols non-equivalent. The printable `r0` family is allocation-order identity, not an exchange identity.
- [`Symbols.cs`](../../../src/Copeland/Copeland.TS/Semantics/Symbols.cs) supplies declaration-ordered `RecordFieldSymbol`, `EnumCaseSymbol`, and named `EnumPayloadFieldSymbol` contracts.
- [`BoundNodes.cs`](../../../src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs) represents record construction/access/update and enum values explicitly. `BoundEnumValueExpression` retains its `EnumCaseSymbol` and ordered arguments. These executable bound expressions are lowering evidence, not TSON nodes.
- Language fixtures under [`Language/Valid/records`](../../../tests/Copeland/Copeland.TS.Tests/Language/Valid/records), [`Language/Invalid/records`](../../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/records), and [`Language/Valid/tagged-data`](../../../tests/Copeland/Copeland.TS.Tests/Language/Valid/tagged-data) prove contextual record construction, same-shaped nominal isolation, field access, `with`, enum construction, and exhaustive payload matching. Invalid record fixtures reject methods, accessors, computed/index-like members, mutation, structural nominal conversion, unknown fields, missing fields, and recursive records.
- [`copeland-ts-language-profile.md`](copeland-ts-language-profile.md) records that general objects, `null`, ordinary `undefined`, ambient optionality, coercion, and unsupported equality families are outside the implemented language.

### Implemented MIR and constant contracts

- [`MirNodes.cs`](../../../src/Copeland/Copeland.TS.Mir/MirNodes.cs) contains executable `MirRecordDefinition`, `MirRecordConstructionExpression`, `MirRecordFieldAccessExpression`, `MirRecordWithExpression`, `MirEnum`, `MirEnumValueExpression`, and matching nodes. [`MirLowerer.cs`](../../../src/Copeland/Copeland.TS/Lowering/MirLowerer.cs) translates the corresponding bound nodes and currently carries compiler IDs such as `r0` into MIR.
- [`MirValidator.cs`](../../../src/Copeland/Copeland.TS.Mir/MirValidator.cs) validates record definitions, field ownership, exact construction, enum/case/payload agreement, cycles, and table constants against the complete executable `MirProgram`.
- `BoundTableConstant` in [`BoundNodes.cs`](../../../src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs) is a closed table-authoring constant family: literal, record, enum, and Result. `MirTableConstant` in [`MirNodes.cs`](../../../src/Copeland/Copeland.TS.Mir/MirNodes.cs) mirrors it. The binder accepts only static deeply immutable cells and recursively rejects unsupported or cyclic cell types.
- [`MirTextWriter.cs`](../../../src/Copeland/Copeland.TS.Mir/MirTextWriter.cs) deterministically renders table constants inside `.cope` MIR expectations. [`MirCorpusTests.cs`](../../../tests/Copeland/Copeland.TS.Tests/MirCorpusTests.cs) treats `.cope` as output-only expected text, and [`Validate-CopelandTsTopology.ps1`](../../../tools/Validate-CopelandTsTopology.ps1) confines `.cope` files to MIR corpus cases. This is useful determinism evidence but not a reusable TSON grammar.
- Tables already admit Results and nominal values because table constants serve table compilation. TSON M0a intentionally does not inherit that domain. The table ladder is stable and remains separate.

### Implemented primitive and backend evidence

- [`Lexer.cs`](../../../src/Copeland/Copeland.TS/Syntax/Lexer.cs) currently accepts only ASCII decimal digit sequences and parses them through `int.TryParse`; unary minus is a bound expression rather than part of the token. The binder's table-constant path converts a negated literal through binary64 and therefore preserves `-0`. This narrow source-literal grammar is implementation residue, while the canonical language profile establishes the broader binary64 value law including NaN, infinities, and signed zero. TSON text must implement its own binary64 grammar rather than reuse the current source lexer.
- The same lexer recognizes single- or double-quoted source strings but skips an escape pair without decoding it and stores the raw interior text. Backend literal writers then escape their received string for their target language. This is evidence that current source escape semantics are incomplete and target-specific; it is not a TSON string codec.
- [`JavaScriptLiteralWriter.cs`](../../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptLiteralWriter.cs) uses invariant round-trip numeric formatting and explicitly escapes quotes, slash, controls, U+2028/U+2029, and UTF-16 surrogate code units. [`CSharpLiteralWriter.cs`](../../../src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpLiteralWriter.cs) is a backend source writer with different formatting rules. Neither defines TSON text.
- [`JavaScriptBackend.cs`](../../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs) realizes enums and Results as private frozen null-prototype `$type`/`$tag`/`$payload` carriers. Records use private type/field symbols, null-prototype objects, and freezing. These objects preserve backend invariants but are neither public ABI nor TSON.
- [`CSharpBackend.cs`](../../../src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs) realizes payload enums as an abstract record with sealed case records and records as generated sealed classes with compiler-owned fields. CLR record equality for enum carriers and reference identity for record carriers do not establish TSON or source equality.
- JavaScript and C# runtime/corpus tests under [`Copeland.TS.Backend.JavaScript.Tests`](../../../tests/Copeland/Copeland.TS.Backend.JavaScript.Tests) and [`Copeland.TS.Backend.CSharp.Tests`](../../../tests/Copeland/Copeland.TS.Backend.CSharp.Tests) prove private representation behavior and backend parity only.

### Similar but separate infrastructure

- `DocumentMir` and Markdown JSON/corpus writers in [`Copeland.Markdown`](../../../src/Copeland/Copeland.Markdown) are document-lane syntax, diagnostic, and artifact infrastructure. They are not Copeland TS typed data.
- Aurelian shader artifacts and the historical VD-MIR documents are shader-lane contracts. They do not justify a shared data IR.
- Repository searches found ordinary JSON artifact code in other products, but no production Copeland TS JSON codec, no TSON code, and no earlier typed-object/schema/data-IR proposal that supplies TSON law.
- Reachable Git history contains direct table-to-JSON recommendations introduced by CTS-TABLE-M0a. Those are historical design decisions now superseded at the architectural routing point: table JSON must eventually pass through an explicitly approved TSON table extension, not bypass TSON.
- No `*.xtest.tsx` or TSPack contract was found in the current tree. Their explicit exclusion prevents a future corpus or package convention from being mistaken for TSON.

## Current-state graph

```text
Copeland TS source
  -> syntax tree
  -> bound program
  -> Cope MIR
     -> deterministic .cope inspection text
     -> private C# generated representation
     -> private JavaScript generated representation

record table source
  -> BoundTableConstant cells
  -> MirTableConstant cells
  -> the same private executable backends
```

There is no native typed-data exchange layer and no JSON implementation in this graph.

## Proposed TSON graph

```text
validated Copeland typed values or dedicated data construction
  -> TSON semantic value
  -> canonical TSON text
  -> optional, explicitly lossy compatibility lowering (future JSON)

canonical TSON text -> parse -> validate -> TSON semantic value
schema-directed TSON shorthand + expected schema
  -> decode and validate -> self-contained TSON semantic value
TSON semantic value + expected Copeland schema
  -> validated nominal Copeland value
```

TSON is a sibling data product of executable Cope MIR. It does not contain functions, statements, locals, control flow, evaluation, or backend storage.

## First-slice value algebra

| Variant | Semantic identity and validation | Immutability and order | Equality status | Canonical text / schema |
| --- | --- | --- | --- | --- |
| Boolean | Exactly `true` or `false`. | Leaf value. | TSON equality API deferred; canonical identity is unambiguous. | `true` or `false`; self-contained. |
| Number | One Copeland binary64 value category. Signed zero is retained; all NaNs have one TSON semantic NaN identity. | Leaf value. | Source numeric equality is not installed as TSON tree equality. | Deterministic spellings below; self-contained. |
| String | A sequence of Unicode scalar values; isolated surrogates are invalid TSON text/value input. | Leaf value, ordinal scalar sequence. | TSON equality API deferred. | Double-quoted and escaped; self-contained. |
| Object | Exact finite ordered fields with unique string names and closed TSON children. No nominal identity. | Immutable; authored order is semantic and canonical order. | Structural object equality is deferred. | `{ name: value }`; self-contained. |
| Record | Stable nominal identity plus exact declaration-ordered named fields validated against its record schema. | Immutable; schema declaration order only. | No source record equality is introduced. | Canonical text carries identity; schema-directed shorthand requires an expected schema. |
| Enum | Stable enum identity, exact case, and exact declaration-ordered named payload fields validated against its enum schema. | Immutable; case payload declaration order only. | No payload-enum equality is introduced. | Canonical text carries enum identity and case; shorthand requires an expected schema. |

No additional primitive leaf is necessary. `void` is not data. Absence has no current Copeland primitive. Arrays, Results, optionality, tables, `null`, `undefined`, dates, blobs, references, functions, classes, symbols, methods, and arbitrary host objects are outside the algebra.

## Structural object law

A TSON object is an immutable ordered structural field map, modeled as a sequence plus a unique-name index. Field names are strings. Field values are closed `TsonValue` children. A value is exact and closed: it contains precisely its listed fields, while being schema-free rather than nominally typed. “Open structural” extension/subtyping is not part of the first slice.

Authored field order is semantic and the canonical printer preserves it. Sorting would erase author intent and would make an object's order differ from record declaration order after conversion. Duplicate names are rejected before a value is published. Objects have no prototype, methods, accessors, mutation, host identity, arbitrary references, or observable sharing.

An object can become a record only through an operation explicitly supplied an expected record schema. That operation validates exact field names and types, rejects duplicates/missing/unknown fields, reorders accepted input to declaration order, and installs the schema's stable nominal identity. Same shape alone is insufficient. If Copeland later gains mutable/general runtime objects, those runtime values remain outside TSON until a separate snapshot/lowering law is approved.

## Nominal record law

A `TsonRecord` consists of:

```text
stable record identity
declaration-ordered (field name, TsonValue) sequence
```

The value is valid only relative to the schema identified by that stable identity. All declared fields are required exactly once; no unknown fields exist; every child validates against its declared field type. Nested records retain their own identities. A record and same-shaped object differ by variant. Two same-shaped records with different stable identities are different nominal values and are not interchangeable.

Canonical text carries the stable record identity. The identity is an opaque canonical string from a future schema namespace, conceptually `copeland://<authority>/<module>#<declaration>`. It must be stable under compiler traversal and declaration ordering. Current `RecordTypeId`/`MirRecordTypeId` values such as `r0`, and field IDs such as `r0.f0`, are compilation-local implementation keys and are prohibited as durable wire identities.

M0b should define the identity value object and syntax, but compiler integration must wait until Copeland defines how a module/schema authority is assigned. A source rename changes a name-derived identity unless a later explicit stable-identity declaration is designed. Identity aliases, versions, migrations, and compatibility negotiation are deferred rather than inferred.

Schema-directed record decoding is a separately named operation. Given an expected record schema it may accept a structural object shorthand, then produces the same self-contained `TsonRecord` that canonical self-described parsing would produce. Empty records are therefore deterministic. No record-language equality, hashing, or ordering follows from validation or canonical printing.

## Payload-enum law

A `TsonEnum` consists of:

```text
stable enum identity
case name
declaration-ordered (payload field name, TsonValue) sequence
```

Validation resolves the enum identity, resolves the case within that enum, then requires exactly the case's payload fields and their types in declaration order. Zero-payload cases carry an empty payload sequence. Record- and enum-valued payloads recursively retain their own identities. Identically named cases in different enums remain isolated by enum identity.

The candidate `Status.Active` is readable but cannot prove durable enum identity. `Shape.Circle({ radius: 4 })` resembles executable calls and makes the payload look like an identity-free object. `Shape.Circle { radius: 4 }` is less call-like but still relies on an unqualified ambient name. Canonical TSON therefore uses an explicit keyword form:

```tson
enum "copeland://example/shapes#Shape" case Circle {
    radius: 4,
}
```

and a zero-payload value is:

```tson
enum "copeland://example/status#Status" case Active {}
```

This is a closed data production, not a function call. A future schema-directed shorthand may permit `Circle { radius: 4 }` only when an expected enum schema is an explicit operation argument. An ambiguous `{ tag, payload }` structural object is not the TSON enum representation.

## Self-description and expected schemas

The recommendation is one semantic model supporting two explicitly different operations:

1. **Self-described parse:** canonical nominal syntax contains every record/enum identity. It returns a self-contained semantic tree after resolving and validating those identities against a supplied schema catalog.
2. **Schema-directed decode:** shorthand input plus an explicit expected schema may omit nominal identities where the expected position determines them. It validates and returns the same self-contained semantic nodes. It never leaves identity implicit in the result.

Canonical printing always emits the self-described form, including nested nominal values. Structural objects remain structural. Object-to-record conversion occurs only in schema-directed decoding. Empty objects, empty records, and zero-payload enum cases remain distinguishable.

The schema catalog is explicit in both operations because names alone cannot validate fields and payload types. “Self-described” means identity is in the text, not that arbitrary schema declarations are embedded. M0a rejects embedded type/schema declarations, imports, and ambient compiler lookup. Stable identity strings make renames/versioning visible; they do not solve them implicitly.

## Canonical textual form

M0a establishes TSON as a semantic model with canonical text. The recommended extension is `.tson`; it is reserved only as design direction here and is not implemented or added to topology.

### Closed grammar direction

One document contains exactly one value and no declarations or statements. Illustrative canonical forms are:

```tson
{
    label: "origin",
    x: -0,
}

record "copeland://example/geometry#Point" {
    x: 1,
    y: 2,
}

enum "copeland://example/shapes#Shape" case Circle {
    radius: 4,
}
```

The grammar admits only the six value variants, field names, punctuation, and primitive tokens. It rejects imports, calls, arbitrary expressions, variables, member evaluation, mutation, control flow, getters, methods, spread, computed properties, prototypes, and evaluation. Existing TypeScript parser acceptance is irrelevant; a future TSON parser owns a smaller grammar.

### Lexical and canonical rules

- Whitespace is insignificant between tokens. Input newlines may be CRLF or LF; canonical output uses LF and ends with one newline.
- Canonical text contains no comments. Comment acceptance is deferred; a parser must not accept them merely because TypeScript does.
- Canonical multi-field values use four-space indentation, one field per line, and a trailing comma. Empty values use `{}` on one line.
- Field names use an unquoted identifier only when they match the closed ASCII identifier grammar `[A-Za-z_][A-Za-z0-9_]*` and are not reserved TSON words. All other names use canonical double-quoted string spelling.
- Strings use double quotes. Canonical escapes are `\"`, `\\`, `\b`, `\f`, `\n`, `\r`, `\t`, and lowercase `\u{...}` for remaining controls or non-printable scalars. Printable Unicode scalars may appear directly. Isolated UTF-16 surrogates are invalid.
- Input is UTF-8. A leading BOM is rejected for canonical text; decoders may diagnose it rather than silently normalize it. Unicode normalization is not performed. Identity and field comparison are ordinal scalar comparison.
- Duplicate fields are syntax/structure errors even if later values would be equal. Source spans cover the offending occurrence and, when useful, cite the first occurrence as related context.
- The parser builds no partially public semantic value: syntax is parsed, structure is validated, schemas are applied, and only then is an immutable root returned.

### Round-trip law

For every valid semantic value `v` and canonical printer `P`, self-described parser/validator `S`, and canonical bytes `b`:

```text
S(P(v)) = v
P(S(b)) = b        when b is canonical
P(S(input))        is the unique canonical normalization of valid noncanonical input
S(P(S(input))) = S(input)
```

The equality signs above denote conformance equivalence defined by variant, primitive identity, nominal identity, field/case names, order, and recursive children. They do not publish a general-purpose `TsonValue.Equals` API and do not add Copeland source equality.

## Primitive and numeric law

| Area | TSON law | Canonical spelling | Future JSON consequence |
| --- | --- | --- | --- |
| Boolean | Two values. | `true`, `false`. | Exact. |
| String | Unicode scalar sequence, no normalization. | Rules above. | Exact modulo JSON escaping; invalid surrogates are already excluded. |
| Finite number | IEEE-754 binary64. Integer-looking text still denotes binary64. | Shortest invariant round-trip decimal; `1`, not `1.0`. | JSON number when the selected JSON policy can preserve the value. |
| Negative zero | Distinct retained TSON numeric identity. | `-0`. Positive zero is `0`. | JSON text can spell `-0`, but consumers may erase the sign; lowering must declare that loss policy. |
| NaN | One canonical TSON NaN identity; payload/sign distinctions are not preserved. | `NaN`. | Unsupported-value diagnostic. |
| Positive infinity | Binary64 value. | `Infinity`. | Unsupported-value diagnostic. |
| Negative infinity | Binary64 value. | `-Infinity`. | Unsupported-value diagnostic. |

Special values are admitted because Copeland's number law admits them and both target runtimes support them. Deterministic spelling avoids inheriting JSON's numeric domain. Numeric grammar does not add hex, binary, separators, bigint, decimal, units, or implicit integer types.

`null` and `undefined` are absent. They are not object placeholders, empty payloads, missing fields, or Result encodings. Optionality remains a future ordinary payload-enum design; absent fields and default fields are not introduced by TSON.

## Tree and immutability law

The first slice is a finite immutable tree. TSON exposes no object identity, shared-reference preservation, cycles, aliases, lazy nodes, executable nodes, accessors, or arbitrary runtime references. A producer may internally share immutable storage only if no API, equality, printer, hash, or lowering can observe it.

Runtime record/enum carriers may have host identity and private slots; lowering snapshots their logical typed value into a TSON tree after validation. The tree law enables bounded validation, deterministic printing and failures, future content hashing, and lossy compatibility lowering without invoking user code. Hashing remains deferred until equality and canonical-byte stability are implemented.

## Compiler lowering boundary

Future compiler integration should use a dedicated typed-data construction/lowering service adjacent to TSON, not lower arbitrary executable MIR and not inspect backend values.

| Flow | Boundary law |
| --- | --- |
| bound record value -> TSON | Consume a validated, data-eligible `BoundRecordConstructionExpression` or a future typed value interface; use stable schema identity and declaration-ordered logical fields. Do not use `r0`. |
| bound enum value -> TSON | Consume `BoundEnumValueExpression`; retain stable enum identity, case, named payload definitions, and ordered values. |
| structural authoring -> TSON | Parse the closed TSON grammar directly; do not route through executable TypeScript syntax/MIR. |
| TSON -> nominal Copeland value | Require an expected schema or matching self-described identity, validate fully, then construct through compiler-owned logical factories. |
| TSON -> compatibility backend | Lower semantic variants under an explicit loss policy; never expose private backend carriers. |

Canonical Cope MIR is suitable evidence of already-validated nominal constructs but is not the preferred universal origin: it contains executable operations and transient IDs, and constant evaluation is not generally available. Bound nodes retain source schema symbols and avoid backend coupling. M0b should not integrate either path; a later compiler milestone must first define stable schema identity and data eligibility without duplicating binder validation.

Diagnostics remain separated:

- invalid Copeland source: existing `COPE-*` frontend diagnostics;
- invalid TSON syntax: TSON parser diagnostics with text spans;
- invalid TSON semantic structure: duplicate/invalid node or tree diagnostics;
- schema mismatch: record/enum identity, field, case, payload, or type diagnostics;
- unsupported compatibility lowering: backend loss/unsupported-value diagnostics.

TSON never becomes a second executable program MIR.

## Relationship to table constants

`BoundTableConstant -> MirTableConstant` supplies three useful precedents: a closed recursive value family, early rejection of executable expressions, and deterministic invariant rendering. Its record/enum variants also prove why nominal identity and declaration order matter.

The types remain separate. Table constants are compiler-owned cells tied to `TypeSymbol`/`MirType`, include Result, and exist inside nominal columnar table definitions. TSON is a backend-neutral exchange tree, excludes Results and tables initially, and needs stable external identities rather than MIR IDs. Replacing the table ladder would destabilize validated frontend, `.cope`, C#, JavaScript, CLI, and topology contracts without a TSON consumer benefit.

A future table-to-TSON milestone may translate eligible table constants only after arrays/tables and Results receive explicit TSON laws. It must not reinterpret `MirTableConstant` as TSON by type alias. Sharing a lower-level immutable-data kernel becomes justified only after two real consumers demonstrate identical value, ordering, identity, validation, diagnostic, and versioning requirements and migration does not weaken the closed table ladder. The default for M0b is no consolidation.

## JSON compatibility boundary

Future JSON is an explicitly lossy compatibility lowering from TSON:

| TSON variant | Future JSON mapping | Information/loss rule |
| --- | --- | --- |
| Boolean | JSON Boolean | None. |
| String | JSON string | TSON ordering/variant retained; escape spelling changes. |
| Number | JSON number or deterministic unsupported-value diagnostic | NaN and infinities unsupported; negative-zero preservation must be an explicit codec guarantee and may be lost by consumers. |
| Object | JSON object | Field order is emitted deterministically but JSON consumers may not treat order as semantic. |
| Record | Schema-directed JSON object | Nominal identity and record-vs-object distinction erased by declared policy; declaration order emitted. |
| Enum | One future canonical tagged JSON representation | Enum identity, and possibly payload naming/order, are erased or encoded by policy. Exact tagged shape remains a bounded future decision. |

JSON also cannot intrinsically distinguish same-shaped record types, a record from an object, or binary64 special values. It has `null`, but TSON does not. No JSON decoder may introduce `null` as Copeland absence.

Future decoding is conceptually:

```text
JSON text
  -> validated untyped JSON value
  -> schema-directed TSON
  -> nominal Copeland value
```

It must reject duplicate keys before a host DOM erases them, apply explicit numeric policies, and never publish private C# or JavaScript carrier objects from arbitrary host-parser output. The JSON enum representation is not finalized by M0a. No JSON implementation belongs in CTS-TSON-M0a or M0b.

## Project and ownership topology

The original recommendation was a BCL-only `src/Copeland/Copeland.TS.Tson/Copeland.TS.Tson.csproj` with a dedicated parser. M0b superseded that recommendation after auditing parser ownership: public syntax contracts already exist in `Copeland.TS`, while extracting them would be broad and a separate parser would violate the one-frontend law. M0b therefore uses a bounded colocated `Copeland.TS.Tson` namespace. See the M0b architecture record for the later extraction criterion.

```text
Copeland.TS.Tson       (BCL only)
       ^
       |
future compiler bridge (owned by Copeland.TS; depends on frontend symbols and TSON)
       ^
       |
Copeland.TS

future JSON compatibility project/namespace -> Copeland.TS.Tson
C# backend  -X-> Copeland.TS.Tson in M0b
JS backend  -X-> Copeland.TS.Tson in M0b
CLI         -X-> Copeland.TS.Tson in M0b
Copeland.TS.Mir -X-> Copeland.TS.Tson
```

TSON must not depend on `Copeland.TS`, `Copeland.TS.Mir`, either backend, CLI, Markdown, Aurelian, reflection, or a host serializer. Owning it inside MIR would make a data format appear executable and expose transient IDs. Owning it inside the frontend would block noncompiler readers and invite symbol leakage. A narrow internal namespace inside `Copeland.TS` would avoid a project but cannot prove the intended backend-neutral exchange boundary. M0b's real parser/printer consumer pair is sufficient to justify the focused project; it does not justify `Copeland.Compiler.Data`, a serialization SDK, or a universal IR.

## Diagnostics and validation

M0b should start with five stable diagnostic families, not dozens of codes:

| Family | Covers |
| --- | --- |
| `TSON-SYNTAX` | invalid token/grammar, invalid string/number, trailing input, noncanonical form when canonical input is required |
| `TSON-STRUCTURE` | duplicate field, depth/size limit, cycle/reference or invalid programmatic node rejection |
| `TSON-SCHEMA` | missing/unknown field, record identity mismatch, enum identity/case mismatch, payload count/name/type mismatch, schema-required ambiguity |
| `TSON-VALUE` | unsupported primitive/value invariant, invalid Unicode, malformed nominal identity |
| `TSON-COMPAT` | later compatibility-lowering loss or unsupported value |

Concrete numeric suffixes may be assigned when M0b implements the first members. Each diagnostic has a stable family/code, deterministic message, primary source span where text exists, optional related span for the first duplicate, and an immutable path such as `.field` or enum payload position for programmatic values. Validation is all-or-nothing; no partially validated root is returned.

## Security and determinism obligations

- Enforce configurable maximum input bytes, nesting depth, field count, and total node count before uncontrolled recursion or allocation. Exact defaults belong to M0b.
- Parse a closed grammar and execute no code. Do not reuse an evaluator or invoke imports, accessors, getters, methods, prototypes, or computed properties.
- Build semantic nodes from parser-owned tokens, never by traversing arbitrary host objects. No reflection, dynamic activation, serializer callbacks, or polymorphic host deserialization is allowed.
- Reject duplicate fields and invalid identities deterministically. Use ordinal comparisons. Preserve object authored order and record/enum declaration order.
- Canonical output is independent of locale, process, backend, hash-table iteration, and newline convention.
- Programmatic construction must apply the same invariants as parsed construction, including finite-tree/cycle protection at untrusted adapter boundaries.
- Publish a root only after complete syntax, structure, schema, and limit validation succeeds.

## Architecture comparisons

| Comparison | Boundary |
| --- | --- |
| TSON vs Cope MIR | TSON is typed data only; Cope MIR is executable compiler IR with functions, control flow, locals, operations, and compilation-local identities. `.cope` is an inspection/corpus projection. |
| TSON vs `MirTableConstant` | TSON is exchange-oriented and initially excludes tables/Results; table constants are compiler-owned validated cells embedded in table MIR and use MIR types/IDs. |
| TSON vs private C# carriers | TSON has backend-neutral semantic variants and no CLR identity/equality/reflection law; generated classes/records are private execution storage. |
| TSON vs private JavaScript carriers | TSON objects are not JS objects; private tokens, symbols, `$tag`, `$payload`, prototypes, freezing, arrays, and host identity do not leak into it. |
| TSON vs JSON | TSON preserves nominal types, record/object distinction, enum identity, order, and binary64 specials; JSON is a later lossy compatibility tree. |
| structural object vs nominal record | Both have exact ordered fields; only a record has stable schema identity and declaration-governed fields/types. Conversion requires an expected schema. |
| semantic identity vs compiler IDs | TSON identity is stable schema/source identity serialized in canonical text; `r0`, `r0.f0`, symbol references, and backend tokens are transient compilation details. |

## Accepted, deferred, and rejected matrix

| Item | Classification | Decision |
| --- | --- | --- |
| boolean | accepted first-slice law | Leaf TSON value. |
| number | accepted first-slice law | Full defined binary64 domain with canonical special spellings. |
| string | accepted first-slice law | Unicode scalar sequence with canonical escaping. |
| structural object | accepted first-slice law | Exact immutable authored-order fields. |
| nominal record | accepted first-slice law | Stable identity plus exact declaration-ordered fields. |
| payload enum | accepted first-slice law | Stable enum/case identity plus ordered named payloads. |
| arrays | explicitly deferred | Needs an independent collection law. |
| Result | explicitly deferred | Must not be inherited from table constants. |
| optionality | explicitly deferred | Future payload-enum language design. |
| `null` | rejected | Absent from Copeland semantics and first algebra. |
| `undefined` | rejected | Absent from Copeland semantics and first algebra. |
| tables | explicitly deferred | Existing table ladder remains separate. |
| functions | rejected | Executable, not data. |
| classes | rejected | Runtime/type behavior, not first-slice data. |
| methods/accessors | rejected | Executable behavior. |
| references/aliases | rejected | First slice is a tree. |
| cycles | rejected | First slice is finite. |
| comments | explicitly deferred | Canonical output has none; input support is not approved. |
| self-described nominal identity | accepted first-slice law | Required in canonical text and semantic nodes. |
| schema-directed decoding | design recommendation awaiting implementation | A distinct operation producing the same self-contained nodes. |
| canonical text | accepted first-slice law | One value, closed grammar, deterministic UTF-8 form. |
| JSON lowering | explicitly deferred | Later lossy compatibility backend. |
| JSON decoding | explicitly deferred | Untyped JSON -> schema-directed TSON -> Copeland. |
| equality | explicitly deferred | Canonical conformance comparison only; no public/source equality. |
| hashing | explicitly deferred | Wait for implemented equality/canonical-byte contracts. |
| reflection | rejected | No arbitrary type discovery or traversal. |
| host-object serialization | rejected | No arbitrary CLR/JS objects or host serializer wrapper. |
| mutable DOM | rejected | Values are validated immutable nodes. |
| embedded schema declarations | rejected | Schemas are external catalogs in first slice. |
| `.tson` extension | design recommendation awaiting implementation | Recommended only; no file association or tooling in M0a. |

## Bounded CTS-TSON-M0b recommendation

CTS-TSON-M0b should be one narrow vertical slice:

1. Create one BCL-only `Copeland.TS.Tson` project and focused test project.
2. Implement immutable semantic contracts for exactly Boolean, Number, String, Object, Record, and Enum; stable opaque nominal identity; finite-tree validation; and explicit schema/catalog interfaces sufficient for record/enum validation.
3. Implement the closed self-described parser and canonical printer defined here, including deterministic binary64/string spelling, spans, resource limits, canonical round-trip fixtures, and the five diagnostic families.
4. Implement a separately named schema-directed decode entry point for object-to-record and expected-enum shorthand, returning the same self-contained nominal nodes.
5. Add no Copeland frontend/MIR lowering, backend carrier conversion, JSON, tables, arrays, Results, optionality, CLI command, generalized serializer, reflection, or package.

This slice proves TSON's defining identity: structural and nominal typed data survive parse/validate/print without relying on JSON or either execution backend. Compiler bridging should be a later milestone because the repository still lacks stable module/schema identity. Table integration must wait for explicit collection/table/Result laws. M0b succeeds only when canonical bytes round-trip across all six variants, same-shaped object/record and record/record identities remain distinct, enum type/case/payload validation is deterministic, and no runtime carrier or transient compiler ID enters the model.
