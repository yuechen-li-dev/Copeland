# CTS-TSON-M0a native typed-data audit

> **Superseded architecture note:** M0a's dedicated-parser, parallel-grammar, external-schema, and independent-project recommendations were corrected by [CTS-TSON-M0b](../Copeland/architecture/copeland-ts-tson-shared-parser-and-semantic-model-cts-tson-m0b.md). The value algebra and TSON-before-JSON direction remain historical input to M0b.

## Outcome

CTS-TSON-M0a is a documentation-only success. The authoritative design is [Copeland TS native typed data: CTS-TSON-M0a design](../Copeland/language/copeland-ts-tson-design-cts-tson-m0a.md).

TSON means **TypeScript Object Notation** as a Copeland project term, not an external TypeScript standard. It is one finite immutable semantic data model with a canonical non-executable textual form. Its first closed algebra is Boolean, binary64 Number, Unicode String, ordered structural Object, nominal Record, and nominal payload Enum.

Canonical text is self-describing for records and enums. A distinct schema-directed decoding operation may accept identity-eliding shorthand but must return the same self-contained nominal semantic nodes. JSON is a future lossy compatibility lowering from TSON. It is not TSON's representation or constitution.

## Audit baseline and scope

The audit began on branch `main` at revision `27e2a3566ace092e9b56fc9a837dd89d17a5c2cc` with a clean worktree. It inspected the actual production source, projects, tests, fixtures, corpus conventions, validators, documentation, and reachable history. No code, tests, fixtures, project files, solutions, tooling, packages, CLI surfaces, extensions, parsers, codecs, or runtime behavior were changed.

Exact production areas and types inspected include:

- syntax/lexing/parsing: `Lexer`, `Parser`, `RecordDeclarationSyntax`, `EnumDeclarationSyntax`, `EnumCaseSyntax`, `ObjectLiteralExpressionSyntax`, `ObjectPropertySyntax`, `WithExpressionSyntax`, `ArrayLiteralExpressionSyntax`, and table syntax in [`Copeland.TS/Syntax`](../../src/Copeland/Copeland.TS/Syntax);
- semantic types/symbols: `PrimitiveTypeSymbol`, `ArrayTypeSymbol`, `ResultTypeSymbol`, `EnumTypeSymbol`, `RecordTypeSymbol`, `RecordTypeId`, `RecordFieldId`, `EnumCaseSymbol`, `EnumPayloadFieldSymbol`, and `RecordFieldSymbol` in [`Types.cs`](../../src/Copeland/Copeland.TS/Semantics/Types.cs) and [`Symbols.cs`](../../src/Copeland/Copeland.TS/Semantics/Symbols.cs);
- binding: contextual object-to-record construction, record access/`with`, enum construction/matching, table constant eligibility, recursive closure, and diagnostics in [`Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs);
- bound model: `BoundRecordDeclaration`, `BoundRecordConstructionExpression`, `BoundRecordFieldAccessExpression`, `BoundRecordWithExpression`, `BoundEnumDeclaration`, `BoundEnumValueExpression`, `BoundMatchExpression`, and the complete `BoundTableConstant` family in [`BoundNodes.cs`](../../src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs);
- MIR: `MirRecordDefinition`, `MirRecordConstructionExpression`, `MirRecordFieldAccessExpression`, `MirRecordWithExpression`, `MirEnum`, `MirEnumValueExpression`, `MirMatchExpression`, and the complete `MirTableConstant` family in [`MirNodes.cs`](../../src/Copeland/Copeland.TS.Mir/MirNodes.cs), plus [`MirLowerer`](../../src/Copeland/Copeland.TS/Lowering/MirLowerer.cs), [`MirValidator`](../../src/Copeland/Copeland.TS.Mir/MirValidator.cs), and [`MirTextWriter`](../../src/Copeland/Copeland.TS.Mir/MirTextWriter.cs);
- runtime realizations: [`JavaScriptBackend`](../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs), [`JavaScriptLiteralWriter`](../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptLiteralWriter.cs), [`CSharpBackend`](../../src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs), and [`CSharpLiteralWriter`](../../src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpLiteralWriter.cs);
- topology: the BCL-only [`Copeland.TS.Mir.csproj`](../../src/Copeland/Copeland.TS.Mir/Copeland.TS.Mir.csproj), frontend/backend project references, [`Validate-CopelandTsTopology.ps1`](../../tools/Validate-CopelandTsTopology.ps1), and [`Validate-DependencyBoundaries.ps1`](../../tools/Validate-DependencyBoundaries.ps1);
- evidence: record, enum, array, Result, absence, and table language fixtures; lexer/binder/MIR corpora; `.cope` expectations; C#/JavaScript corpus/runtime tests; table malformed-MIR cases; and CLI integration tests;
- adjacent infrastructure: `DocumentMir`, Markdown parsing/dumping/corpus JSON, shader artifacts, VD-MIR doctrine, and repository JSON usages.

No TSON implementation or prior TSON proposal was found. No `*.xtest.tsx`/TSPack contract was found. Direct table-JSON recommendations originate in the table design/history; they are preserved as historical context but rerouted prospectively through TSON.

## Implemented contracts versus proposals

| Finding | Classification | TSON consequence |
| --- | --- | --- |
| Contextual braces construct an expected nominal record; uncontextualized objects are rejected. | implemented language contract | TSON adds structural objects only in the data layer; it does not add general Copeland runtime objects. |
| Records have nominal symbols, exact fields, declaration order, field reads, and same-type `with`. | implemented language contract | TSON records retain stable nominal identity and exact declaration-ordered fields. |
| Payload enums retain enum, case, named payload definitions, ordered arguments, and exhaustive match. | implemented language contract | TSON enums retain all three identities and ordered named payloads. |
| `r0`/`r0.f0` identify records/fields through bound and MIR lowering. | implemented compiler mechanism | They are transient allocation-order keys and prohibited as durable TSON identities. |
| `.cope` is deterministic MIR output and corpus expectation; it is not parsed as source. | implemented tooling/corpus contract | TSON text is a separate closed data grammar and does not replace `.cope`. |
| `BoundTableConstant -> MirTableConstant` is closed, deeply immutable, validated, and deterministically rendered. | implemented table contract | Reusable evidence only; no consolidation or type reuse in M0a/M0b. |
| JS records/enums/Results use private frozen carriers and symbols/tags/payload arrays. | implemented backend detail | Runtime carriers are not TSON nodes or a public serialization ABI. |
| C# records/enums/Results use generated classes/record cases/generic carriers. | implemented backend detail | CLR equality, reflection, and object layout do not define TSON. |
| Binary64 includes NaN, infinities, and signed zero; string writers already differ by backend. | normative language law plus backend evidence | TSON owns deterministic numeric and string text independent of either writer and JSON. |
| The source lexer accepts digit-only `int` literals and preserves rather than decodes string escape pairs; unary minus is separately bound. | implemented frontend limitation | TSON requires its own binary64 and string grammar; current lexer behavior is not promoted into the data format. |
| Table docs select direct schema-directed columnar JSON. | historical accepted table direction, unimplemented | Superseded only in routing: future table compatibility must first gain a TSON table law, then lower to JSON. Exact JSON enum shape is reopened. |
| Markdown JSON, `DocumentMir`, shader artifacts, and VD-MIR exist. | separate product/lane contracts | No shared serializer or universal IR extraction. |

## Decisions

- **Definition:** semantic data IR plus canonical text as one model; neither runtime object nor text-only serialization.
- **Algebra:** Boolean, Number, String, Object, Record, Enum; nothing else.
- **Object/record:** both are exact immutable ordered field trees, but only Record has stable schema identity and declaration-governed validation. Object-to-record conversion requires an expected schema.
- **Enums:** stable enum identity, case identity, and exact declaration-ordered named payloads are semantic state. Zero-payload is an empty payload sequence. `{ tag, payload }` is not the semantic representation.
- **Identity:** canonical text is self-described. A separate schema-directed decode may accept shorthand. No identity is silently inferred from ambient compiler state.
- **Text:** one `.tson` value is recommended, with no embedded schemas, execution, imports, calls, references, or comments in canonical output. The extension is not implemented by M0a.
- **Numbers:** full Copeland binary64 domain, shortest round-trip finite spelling, `-0`, `NaN`, `Infinity`, and `-Infinity`. JSON later diagnoses non-finite values. TSON canonicalizes all NaNs to one semantic NaN category.
- **Absence:** no `null` or `undefined`; optionality remains a future payload-enum design.
- **Tree:** finite, immutable, identity-free, acyclic, eager data; no host graph traversal.
- **Tables:** remain separate. Future table-to-TSON translation needs explicit TSON arrays/tables/Results law and graduation evidence.
- **Ownership:** future BCL-only `Copeland.TS.Tson`; no dependency on frontend, MIR, backends, CLI, reflection, or host serializers.
- **JSON:** future `JSON -> validated untyped JSON -> schema-directed TSON -> nominal Copeland value`; encoding lowers TSON under an explicit loss policy.

## Anti-conflation result

The design explicitly excludes Cope MIR, `.cope`, `*.xtest.tsx`, TypeScript AST dumps, JavaScript object semantics, JSON-family formats, host serializers, `DocumentMir`, VD-MIR, arbitrary language/CLR IRs, reflection graphs, target providers, mutable DOMs, and speculative shared compiler infrastructure. TSON belongs only to the Copeland TS typed-data lane.

## First-slice classification summary

Accepted laws are the six variants, finite immutable trees, self-described stable nominal identity, exact ordered fields/payloads, canonical text, and deterministic validation. Schema-directed decoding and the `.tson` extension are implementation recommendations. Arrays, Results, optionality, tables, comments, equality, hashing, JSON encoding/decoding, and all compiler/backend integration are deferred. `null`, `undefined`, functions, classes, methods, references, cycles, reflection, arbitrary host objects, embedded schemas, and a mutable DOM are rejected.

The complete required matrix and architecture comparisons are in the [authoritative design](../Copeland/language/copeland-ts-tson-design-cts-tson-m0a.md#accepted-deferred-and-rejected-matrix).

## CTS-TSON-M0b

The implemented next milestone is narrowly bounded to the isolated `Copeland.TS.Tson` namespace colocated in the frontend assembly. It implements exactly the six immutable semantic variants, stable schema-derived nominal identity, schema/catalog validation, resource-bounded projection from the production syntax tree, canonical printing, spans/diagnostics, canonical round-trip fixtures, and distinctly named self-described and authoring decode entry points.

M0b adds no MIR lowering, backend carrier conversion, JSON, tables, arrays, Results, optionality, CLI command, generalized serializer, reflection, or package. Colocation is the smallest safe shared-parser boundary because the public `SyntaxTree.Parse` facade and syntax nodes already supply everything the restriction pass needs; syntax extraction is deferred until another consumer requires an assembly boundary.

## Documentation routing

The canonical language profile now records TSON as the prerequisite typed-data direction for future serialization. The table M0a design and M3 closeout retain their original unimplemented JSON discussion but mark direct table-to-JSON routing as superseded: table support requires a separately approved TSON extension before JSON compatibility lowering.

## Validation contract

This milestone requires documentation-only scope, `git diff --check`, exact diff inspection, Markdown link/path/table/fence/terminology/trailing-whitespace checks, and both Copeland topology/dependency validators. Full builds and tests are intentionally excluded because no non-document file changes.
