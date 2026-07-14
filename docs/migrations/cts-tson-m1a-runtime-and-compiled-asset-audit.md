# CTS-TSON-M1a runtime and compiled-asset audit

## Outcome

CTS-TSON-M1a is a documentation-only architecture success. The authoritative design is [Copeland TS TSON value projection: CTS-TSON-M1a design audit](../Copeland/language/copeland-ts-tson-value-projection-design-cts-tson-m1a.md).

The selected next product operation is compile-time ingestion of one self-described `.obj.ts` or canonical `.tson` root into an explicitly typed nominal Copeland record or payload enum. Runtime encoding has an accepted reflection-free generated-projector direction but is deferred. Runtime text/byte decoding is blocked until one parser architecture exists for both generated C# and JavaScript; “reuse the parser” is not an implementation answer because the only production parser is in the compiler-host .NET assembly.

## Baseline

Work began from clean revision `da0945e6732a2b238178c005a2da297bf830373a` on branch `main`, tracking `origin/main` at 0 ahead and 0 behind. This milestone preserves all CTS-TSON-M0a/M0b work and changes documentation only.

## Audited implementation

The audit inspected these exact production surfaces:

- TSON compiler-host model: `TsonValue`, `TsonBoolean`, `TsonNumber`, `TsonString`, `TsonObject`, `TsonRecord`, `TsonEnum`, and `TsonField` in [`TsonValues.cs`](../../src/Copeland/Copeland.TS/Tson/TsonValues.cs);
- schema/catalog: `TsonTypeReference`, `TsonFieldDefinition`, `TsonRecordDefinition`, `TsonEnumCaseDefinition`, `TsonEnumDefinition`, and `TsonCatalog` in [`TsonSchema.cs`](../../src/Copeland/Copeland.TS/Tson/TsonSchema.cs);
- profiles/results/limits: `TsonDocumentProfile`, `TsonLimits`, `TsonDiagnostic`, `TsonDocument`, and `TsonReadResult` in [`TsonDocument.cs`](../../src/Copeland/Copeland.TS/Tson/TsonDocument.cs);
- shared-parser projection: `TsonDocumentReader.ReadSelfDescribed`, `DecodeAuthoringValue`, catalog construction, cycle checking, schema identity derivation, expected-type validation, string decoding, bounds, and canonical-input comparison in [`TsonDocumentReader.cs`](../../src/Copeland/Copeland.TS/Tson/TsonDocumentReader.cs);
- parser/printer: `SyntaxTree.Parse` in [`SyntaxTree.cs`](../../src/Copeland/Copeland.TS/Syntax/SyntaxTree.cs) and `TsonCanonicalPrinter.Print`/`PrintUtf8` in [`TsonCanonicalPrinter.cs`](../../src/Copeland/Copeland.TS/Tson/TsonCanonicalPrinter.cs);
- source/bound types: `RecordTypeSymbol`, `RecordTypeId`, `RecordFieldId`, `EnumTypeSymbol`, `ResultTypeSymbol`, declaration-ordered field/case/payload symbols, record/enum construction, Result, propagation, unwrap, `try`/`except`, and closed table constants in [`Types.cs`](../../src/Copeland/Copeland.TS/Semantics/Types.cs), [`Symbols.cs`](../../src/Copeland/Copeland.TS/Semantics/Symbols.cs), [`Binder.cs`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs), and [`BoundNodes.cs`](../../src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs);
- MIR boundary: record, enum, Result, propagation, unwrap, try, and table nodes in [`MirNodes.cs`](../../src/Copeland/Copeland.TS.Mir/MirNodes.cs), lowering in [`MirLowerer.cs`](../../src/Copeland/Copeland.TS/Lowering/MirLowerer.cs), and exact/closed validation in [`MirValidator.cs`](../../src/Copeland/Copeland.TS.Mir/MirValidator.cs);
- generated C#: sealed record classes, internal complete constructors/get-only members, abstract enum records/sealed case records, Result helper, unwrap panic, table constants, and demand-emitted helpers in [`CSharpBackend.cs`](../../src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs);
- generated JavaScript: closure-private Symbols/type tokens, frozen null-prototype records, frozen enum/Result carriers, validators, terminal invariant panic, and table realization in [`JavaScriptBackend.cs`](../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs);
- compiler/CLI/topology: [`CopelandCompiler.cs`](../../src/Copeland/Copeland.TS/Compiler/CopelandCompiler.cs), [`CopelandCompilationOptions.cs`](../../src/Copeland/Copeland.TS/Compiler/CopelandCompilationOptions.cs), [`Program.cs`](../../src/Copeland/Copeland.Cli/Program.cs), all four Copeland TS project files, [`Validate-CopelandTsTopology.ps1`](../../tools/Validate-CopelandTsTopology.ps1), and [`Validate-DependencyBoundaries.ps1`](../../tools/Validate-DependencyBoundaries.ps1).

The audit used exact evidence from `TsonFeatureTests`, `TsonFixtureTests`, record language fixtures, payload-enum fixtures/corpora, `RecordRuntimeTests`, `JavaScriptRuntimeTests`, `ResultBackendParityTests`, `MalformedTableConstantValidationTests`, both backend corpus suites, and CLI integration tests. The implemented laws are also recorded in CTS-TSON-M0a/M0b, the canonical language profile, immutable-record closeout, payload-enum/runtime documents, typed-fallibility closeout, record-table closeout, and compiler-topology doctrine.

## Current boundary

`Copeland.TS.Tson` is a namespace colocated in `Copeland.TS`, not a runtime package. The same assembly also contains the source parser, binder, compiler facade, and lowering and references `Copeland.TS.Mir`. Generated C# and JavaScript do not reference it. The canonical printer and parser are compiler-host-only. Directly shipping `Copeland.TS` could make the parser available to C# but would pull compiler boundaries into the application, lacks a NativeAOT size/trimming proof, and would not make a parser available in JavaScript.

The CLI reads one source file and composes frontend, MIR, and backend projects. There is no import/module resolver, asset graph, package identity, or implemented generic `decode<T>` surface. `ModuleName` is not a schema or module law.

## Decisions

- **Next operation:** operation E, compile-time TSON asset to compiled nominal value.
- **Runtime parser:** defer runtime decode. Reject a second handwritten parser and reject C#-only compiler-parser shipping as the product architecture. Schema-specific decoding still needs a grammar parser.
- **Stable identity:** require one explicit literal compilation-unit `$schema` directive and derive M0b identities. Never use `r0`, MIR IDs, CLR names, namespaces, assemblies, JavaScript names, npm metadata, or hashes.
- **Source API:** use `tsonAsset("./path.obj.ts|.tson")` as a compiler-only expression requiring an immediately expected annotated nominal type. Do not use import syntax or a runtime-looking `load` call.
- **Generated strategy:** validate in the compiler host, translate to existing bound primitive/record/enum construction, then use existing Cope MIR and legitimate complete backend constructors. `TsonValue` does not enter MIR or generated code.
- **Runtime encoding direction:** later generated type-specific record/enum projectors should call a small demand-emitted canonical writer. C# reads known internal members and dispatches closed case records; JavaScript code lives beside private Symbols/tokens/constructors. No reflection, member enumeration, host-object traversal, or JSON writer.
- **Structural objects:** remain TSON-document-only because Copeland has no runtime structural-object type. Contextual braces prove nominal construction only.
- **Errors:** M1b uses compiler diagnostics and existing `COPE-TSON-0001` through `0005`; future runtime encoding needs only a bounded resource-limit error, and future decode needs syntax, canonicality, schema, and resource-limit variants returned as `Result`.
- **Parity:** canonical equivalence means identical Unicode scalar/string contents and identical BOM-free UTF-8 bytes, including LF/final newline, escaping, uppercase binary64 bits, declaration order, and rejection category.
- **JSON:** remains future TSON compatibility lowering. No host JSON behavior is selected.

## Classification summary

Implement in M1b: `.obj.ts` and `.tson` compile-time ingestion, explicit `$schema` identity, typed `tsonAsset`, exact nominal/schema validation, and lowering through existing constructors on both backends.

Accepted but deferred: runtime record/enum text encoding, generated projectors, a bounded writer, strict UTF-8 bytes as a separate API, and arrays/Results/tables/optionality/JSON only after their graduation laws. A shared runtime library remains premature.

Unresolved blocker: one runtime parser architecture that is genuinely available and conformant in both C# and JavaScript.

Rejected: runtime structural-object encoding, exposed compiler-host `TsonValue`, compiler parser as a C#-only runtime solution, a second handwritten TSON parser, import syntax before modules, reflection, `dynamic`, dictionaries as universal codecs, arbitrary host objects, property enumeration, private carrier serialization, and direct table-to-JSON.

## Exact M1b boundary

M1b should accept one embedded-identity `.obj.ts` or canonical `.tson` root from a project-relative literal `tsonAsset` site, match it to a stable explicitly identified record or payload enum, recursively permit only Boolean/Number/String/Record/Enum, translate it into existing bound construction expressions, and prove deterministic C#/JavaScript consumption without runtime parsing or new dependency edges. Multiple roots/exports, imports, global/singleton semantics, runtime encoding/decoding, bytes, public TSON nodes, structural objects, arrays, Results, tables, optionality, packages, reflection, and JSON are out of scope.

## Changed documentation

- [`copeland-ts-tson-value-projection-design-cts-tson-m1a.md`](../Copeland/language/copeland-ts-tson-value-projection-design-cts-tson-m1a.md)
- this migration record
- the [canonical Copeland TS language profile](../Copeland/language/copeland-ts-language-profile.md), updated narrowly to route TSON follow-up through M1a
- the [Copeland documentation index](../Copeland/README.md), updated with M1a links

No historical M0a/M0b, table, runtime, or JSON document required a semantic correction beyond this prospective routing.

## Validation

Validation passed for `git diff --check`, exact diff and documentation-only scope inspection, local Markdown link/path checks, heading-anchor checks, table/fence/trailing-whitespace/terminology checks, `tools/Validate-CopelandTsTopology.ps1`, and `tools/Validate-DependencyBoundaries.ps1`. The topology validator reported success; the dependency validator reported success for 27 production projects with no exceptions permitted. Full builds/tests were intentionally not run because no non-document file changed.
