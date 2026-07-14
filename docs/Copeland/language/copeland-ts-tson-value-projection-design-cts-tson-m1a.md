# Copeland TS TSON value projection: CTS-TSON-M1a design audit

**Status:** accepted documentation-only architecture milestone; its selected compile-time operation is implemented by [CTS-TSON-M1b](../architecture/copeland-ts-compile-time-tson-assets-cts-tson-m1b.md). M1a itself changed no production code.

## Executive decision

The next useful product operation is **compile-time ingestion of one self-described `.obj.ts` or canonical `.tson` root into an explicitly typed Copeland value**. Runtime encoding has an accepted generated-projector direction but is deferred. Runtime text or byte decoding is blocked because the production Copeland parser exists only in the compiler-host `Copeland.TS` assembly and there is no equivalent parser in generated JavaScript. M1b must not add a second TSON parser or ship the compiler frontend as an application runtime.

The proposed first source form is an unmistakably compile-time compiler intrinsic used with an expected nominal type:

```ts
const $schema: string = "copeland://example/app/settings";

record Settings {
    enabled: boolean;
    retryCount: number;
}

function settings(): Settings {
    const value: Settings = tsonAsset("./settings.tson");
    return value;
}
```

`$schema` is an explicit compilation-unit identity directive, not a runtime binding. It gives ordinary declarations the exchange identities `schema#Type`, `schema#Type.field`, `schema#Enum.Case`, and `schema#Enum.Case.payload`. `tsonAsset` is not an ordinary library call and never performs runtime file access. Import syntax is not selected because modules, imports, exports, multi-file binding, and cross-file initialization are unresolved.

M1b should parse the asset with `TsonDocumentReader`, require its root identity and reachable schema to match the expected compiled nominal type, and translate the root into the existing bound record/enum/literal construction family. Existing lowering then produces ordinary Cope MIR and existing backends construct their legitimate nominal carriers. No `TsonValue` crosses into MIR or the generated application. The first slice permits only a nominal record or payload-enum root with nested Boolean, Number, String, Record, and Enum values. Structural objects, arrays, Results, tables, optionality, runtime codecs, bytes, JSON, and multiple asset exports remain outside it.

## Audit baseline and evidence classification

This audit began from clean revision `da0945e6732a2b238178c005a2da297bf830373a` on branch `main`, tracking `origin/main` at 0 ahead and 0 behind. Statements below distinguish current implementation from accepted direction.

### Implemented TSON foundation

- [`TsonValues.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonValues.cs) defines the public compiler-host `TsonValue` base and closed `TsonBoolean`, `TsonNumber`, `TsonString`, `TsonObject`, `TsonRecord`, and `TsonEnum` variants. `TsonField` carries optional nominal field identity; constructors defensively copy and reject invalid identities or duplicate names.
- [`TsonSchema.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonSchema.cs) defines `TsonTypeKind`, `TsonTypeReference`, `TsonFieldDefinition`, `TsonRecordDefinition`, `TsonEnumCaseDefinition`, `TsonEnumDefinition`, and `TsonCatalog`. A catalog owns one schema identity and ordered nominal definitions.
- [`TsonDocument.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonDocument.cs) defines `TsonDocumentProfile.ObjectTypeScript`, `TsonDocumentProfile.CanonicalTson`, `TsonLimits`, `TsonDiagnostic`, `TsonDocument`, and `TsonReadResult`.
- [`TsonDocumentReader.ReadSelfDescribed`](../../../src/Copeland/Copeland.TS/Tson/TsonDocumentReader.cs) checks source and nesting bounds, calls [`SyntaxTree.Parse`](../../../src/Copeland/Copeland.TS/Syntax/SyntaxTree.cs), restricts the ordinary syntax tree, constructs the catalog and root, validates nominal closure, and, for `CanonicalTson`, compares the input to the canonical printer output. `DecodeAuthoringValue` supplies an authoring schema identity for the permissive profile; it is compiler-host authored-data decoding, not decoding into a compiled program value.
- [`TsonCanonicalPrinter`](../../../src/Copeland/Copeland.TS/Tson/TsonCanonicalPrinter.cs) emits LF-only text ending in LF, four-space indentation, sorted declaration names, declaration-ordered fields and payloads, double-quoted escaped strings, and `$number("XXXXXXXXXXXXXXXX")`. `PrintUtf8` uses `Encoding.UTF8`, which is BOM-free for returned bytes.
- [`TsonFeatureTests`](../../../tests/Copeland/Copeland.TS.Tests/TsonFeatureTests.cs) proves shared-parser profile round trips, complete binary64 categories and normalized NaN, stable distinct nominal identities, restriction failures, catalog/type failures, parser diagnostic retention, canonicality, every resource limit, defensive copying, and Unicode surrogate-pair handling. [`TsonFixtureTests`](../../../tests/Copeland/Copeland.TS.Tests/TsonFixtureTests.cs) owns valid `.obj.ts`/`.tson` and invalid filesystem fixtures under [`Tson`](../../../tests/Copeland/Copeland.TS.Tests/Tson).
- [CTS-TSON-M0a](copeland-ts-tson-design-cts-tson-m0a.md) establishes the six-value algebra, finite immutable trees, nominal distinction, and TSON-before-JSON law. [CTS-TSON-M0b](../architecture/copeland-ts-tson-shared-parser-and-semantic-model-cts-tson-m0b.md) corrects M0a's parser/project proposal and records the implemented shared-parser profiles, envelope, exact identities, bounds, and exclusions.

### Implemented compiler and runtime boundary

- [`Copeland.TS.csproj`](../../../src/Copeland/Copeland.TS/Copeland.TS.csproj) contains the `Copeland.TS.Tson` namespace in the frontend assembly and references `Copeland.TS.Mir`. TSON is not a separate runtime assembly.
- [`Copeland.TS.Mir.csproj`](../../../src/Copeland/Copeland.TS.Mir/Copeland.TS.Mir.csproj) remains the BCL-only frontend/backend contract. Both backend projects reference MIR, not `Copeland.TS`; the CLI references the frontend, MIR, and both backends and owns their composition.
- [`CopelandCompiler.Compile`](../../../src/Copeland/Copeland.TS/Compiler/CopelandCompiler.cs) accepts one source string, parses, binds, and lowers it. `CopelandCompilationOptions.ModuleName` exists but is only copied through the facade; it supplies no module/import/schema law. [`Program.RunCompile`](../../../src/Copeland/Copeland.Cli/Program.cs) reads one source path and emits MIR, C#, or JavaScript. There is no project-relative asset resolver or dependency graph.
- The parser has no import declaration syntax or module resolver. The TSON fixture `import.obj.ts` is rejected as non-data, and `CL-MODULE-001` in the [canonical language profile](copeland-ts-language-profile.md) marks module/import/export semantics unresolved.
- Generated C# and JavaScript source contains demand-emitted backend helpers, but no reference to `Copeland.TS`, `TsonValue`, `TsonCanonicalPrinter`, or the production parser. Therefore generated programs cannot currently construct compiler-host TSON nodes or parse/print TSON.

### Implemented nominal value evidence

- [`Types.cs`](../../../src/Copeland/Copeland.TS/Semantics/Types.cs) contains primitive, array, `ResultTypeSymbol`, nominal `RecordTypeSymbol`, and nominal `EnumTypeSymbol` types. `RecordTypeId` and `RecordFieldId` print allocation-local `rN` identities. They are compiler keys, not exchange identities. There are no source-language generic type parameters.
- [`Symbols.cs`](../../../src/Copeland/Copeland.TS/Semantics/Symbols.cs) retains declaration-ordered `RecordFieldSymbol`, `EnumCaseSymbol`, and `EnumPayloadFieldSymbol` definitions.
- [`BoundNodes.cs`](../../../src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs) explicitly represents `BoundRecordConstructionExpression`, field access and `with`, `BoundEnumValueExpression`, Result construction/match, `BoundPropagateExpression`, `BoundUnwrapExpression`, and `BoundTryExceptExpression`. Its closed `BoundTableConstant` family is useful future constant evidence but includes Result/table-specific laws that TSON must not inherit.
- [`MirNodes.cs`](../../../src/Copeland/Copeland.TS.Mir/MirNodes.cs) has corresponding record, enum, Result, propagation, unwrap, try, and table nodes. [`MirLowerer`](../../../src/Copeland/Copeland.TS/Lowering/MirLowerer.cs) carries `RecordTypeId` into `MirRecordTypeId`; [`MirValidator`](../../../src/Copeland/Copeland.TS.Mir/MirValidator.cs) validates exact record ownership/construction, enum case/payload agreement, Result flow, handler targeting, and closed table constants before either backend.
- [`CSharpBackend.EmitRecord`](../../../src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpBackend.cs) emits a sealed class with an internal complete constructor and internal get-only ID-derived members. `EmitEnum` emits an abstract record with sealed nested case records. `RecordRuntimeTests.Same_shaped_records_have_distinct_generated_nominal_types` and `ResultBackendParityTests.JavaScript_And_CSharp_Record_Closeout_Matrix_Preserves_Control_Flow_And_Exactly_Once_Order` prove nominality and parity, not TSON.
- [`JavaScriptBackend.EmitRecordRuntime`](../../../src/Copeland/Copeland.TS.Backend.JavaScript/JavaScriptBackend.cs) emits closure-private type and field `Symbol`s, null-prototype values, non-enumerable read-only slots, freezing, and a validator. Payload enums use a private type token plus frozen `$tag`/`$payload` carriers. `JavaScriptRuntimeTests.Node_Proves_Record_Nominality_Immutability_And_Representation_Isolation` and `Node_Executes_Payload_Enum_Match_Repeatedly` prove the private representation.
- The [typed fallibility closeout](../architecture/copeland-ts-typed-fallibility-closeout-cts-m6d.md) establishes typed `Result`, `?`, unwrap, and lexical `try`/`except`. Ordinary failure is explicit Result flow; host exceptions are not caught by `except`; unwrap/backend invariant failures are terminal.
- The [record-table closeout](../architecture/copeland-ts-record-tables-closeout-cts-table-m3.md) proves immutable static column realization and cross-backend constants. It remains future evidence only: private table carriers are not exchange values and table-to-JSON cannot bypass an approved TSON array/Result/table law.

## Operation taxonomy A-E

The five operations are separate products and must remain separately named.

| Operation | Input and output | Current status | Runtime requirement | M1a decision |
| --- | --- | --- | --- | --- |
| A | authored `.obj.ts`/`.tson` source -> compiler-host `TsonDocument`/`TsonValue` | Implemented by M0b | Compiler parser and frontend assembly | Retain unchanged. |
| B | compiled Copeland value -> TSON semantic value | Not implemented | Generated projection plus a chosen runtime semantic carrier | Design accepted; no public `TsonValue` in M1b. |
| C | compiled Copeland value -> canonical TSON text/bytes | Not implemented | Generated projection and exact writer; bytes add UTF-8 policy | Text direction accepted but deferred; bytes separately deferred. |
| D | canonical TSON text/bytes -> compiled Copeland value | Not implemented | Runtime parser, schema validation, and closed constructor access | Blocked on a cross-backend one-parser architecture. |
| E | compile-time TSON asset -> generated compiled value | Not implemented | Compiler-host parser only; generated nominal construction | Implement in M1b for one nominal root. |

An API named merely `serialize` or `load` would hide these differences and is rejected.

## Current compiler-host authored-data path

```text
.obj.ts or .tson text
  -> Copeland.TS.Syntax.SyntaxTree.Parse
  -> Copeland.TS.Tson restriction + catalog/schema validation
  -> immutable compiler-host TsonDocument/TsonValue
  -> compiler-host TsonCanonicalPrinter
  -> canonical LF text or BOM-free UTF-8 bytes
```

This path is implemented operation A. It stops before binding, Cope MIR, either backend, or a generated application.

## Compiler-host TSON versus runtime TSON

### Exact current ownership

`TsonValue`, `TsonDocument`, the reader, schemas, limits, diagnostics, and printer all live in the `Copeland.TS` assembly. That assembly also owns lexer/parser syntax, binder, lowering, and compiler facade and references `Copeland.TS.Mir`. Reusing it directly in a C# application would pull the compiler frontend and MIR assembly boundary into the application. It would also make NativeAOT size and trimming properties an application concern without a publish proof. It does nothing for JavaScript or browser parity because the C# compiler assembly is not emitted or translated to JavaScript.

Generated C# currently uses ordinary `System` types and generated helpers. Generated JavaScript is standalone closure-scoped code. Neither can construct compiler-host `TsonValue`; neither should gain that capability merely by exposing compiler internals.

### Options

| Option | Benefit | Cost or boundary failure | Decision |
| --- | --- | --- | --- |
| Share exact compiler-host semantic contracts | One .NET semantic model and printer | Pulls frontend/MIR into C# apps; unavailable to JS/browser; does not prove NativeAOT | Rejected for M1b and for JS parity. |
| Backend-private runtime TSON nodes | Closed and reflection-free | Duplicates a semantic tree and needs explicit translation/parity proof | Deferred until an operation needs an inspectable runtime value. |
| Emit canonical text directly with type-specific code | No public DOM and no host traversal | Writer algorithms can drift unless parity tests own every rule | Accepted runtime-encoding direction with generated projectors and a small closed writer. |
| Small portable TSON runtime library | Could centralize nodes/writer for .NET | Cannot itself be shared as CLR metadata with JS; packaging is premature | Deferred until runtime encoding creates a concrete dependency. |
| Compile-time-only ingestion | Uses the existing parser and emits ordinary values | No runtime text input or encode API | Selected M1b vertical slice. |

The compiler-host semantic model remains the authoritative operation-A model. A future runtime writer must implement the same normative format contract, not expose or duplicate compiler-host classes by accident.

## Runtime-parser analysis

### A. Compile-time-only parsing

This is supported by the current architecture. The compiler already parses and validates both profiles. Generated output can contain only validated primitive and nominal construction. C#, JavaScript, browser execution, and NativeAOT then need no parser or compiler assembly. Product consequence: programs may consume packaged configuration known at build time, but cannot accept TSON supplied after compilation.

### B. Ship the existing compiler parser in C# applications

`SyntaxTree.Parse` is public, but its implementation is in the frontend assembly alongside binding/lowering/TSON and references the MIR project. Direct reuse increases deployment surface, has no NativeAOT/trimming/size proof, and violates the desired runtime/compiler boundary. More decisively, it produces no JavaScript parser. Backend-specific availability would create different source capabilities. Reject this as the runtime decode architecture.

### C. Port or compile the parser for both runtimes

The repository has no self-hosted Copeland compiler, portable parser IR, generated grammar, or C#-to-JavaScript compilation path. Treating such work as an M1b prerequisite would be speculative self-hosting and an overbroad compiler project. Defer it.

### D. Dedicated runtime TSON parser

A handwritten runtime parser would be a second grammar implementation and violate the one-parser doctrine. It remains rejected unless a future milestone first proves one generated/shared grammar artifact whose conformance prevents drift on both backends.

### E. Schema-specific decoders

Generated schema validators and constructors can eliminate reflection and dynamic type lookup, but they do not eliminate grammar parsing. A general token/parse layer must still turn text into the six TSON variants before schema validation. Schema-specific decoding solves the second arrow, not the first.

### F. Defer runtime decoding

Selected. M1b implements operation E only. Runtime encoding can follow without a parser. Runtime decoding remains unavailable, and no API should pretend otherwise.

## Candidate compile-time asset path

```text
typed tsonAsset("./settings.tson") site
  -> compiler-host project-relative resolver
  -> existing SyntaxTree.Parse + TsonDocumentReader profile
  -> expected stable identity + reachable-schema validation
  -> existing bound primitive/record/enum construction expressions
  -> existing MirLowerer + MirValidator
  -> existing C# constructor/member or JS private-symbol constructor path
  -> immutable compiled value; no runtime parser
```

### Source experience selection

`import settings from "./settings.tson"` and its `.obj.ts` form are rejected for M1b because current import/export/module parsing, binding, initialization, and dependency semantics do not exist. Selecting asset imports would accidentally design modules.

`tsonAsset("./settings.tson")` is selected as a reserved compile-time expression form using existing call and string-literal syntax. The compiler must diagnose it as a special form; no ordinary function symbol named `tsonAsset` exists, and generated code never contains a call by that name. The expression requires an immediate expected nominal type, initially a declared local `const` annotation. It is not permitted in a dynamically selected path argument, ordinary runtime call, untyped variable, or host API.

`Tson.load` is rejected because it reads like runtime I/O. `Tson.decode` and `decode<T>` are rejected for M1b: runtime parsing is unavailable, general generic declarations/calls are not implemented, and type values are not source values.

### Resolution and dependency law

- The compilation request must carry the primary source file path and a project root. The current string-only facade can remain for source-only compilations; an asset-bearing compilation must use a path-aware request. The existing CLI `compile <source-file>` can supply this context without a new command.
- Asset paths are compile-time string literals, normalized relative to the importing source file, and must resolve inside the declared project root. Absolute paths, root escape after canonical path resolution, unsupported extensions, missing files, and duplicate logical paths are deterministic compiler diagnostics.
- `.obj.ts` participates as data, never as executable source. For M1b it must embed `$schema`; the compiler does not inject an ambient identity. It may contain comments and noncanonical layout accepted by `ObjectTypeScript`.
- `.tson` uses `CanonicalTson` and must match exact canonical text. It is not silently reformatted or accepted with a BOM, CRLF, missing final LF, or alternate escapes.
- Each intrinsic creates a source-to-asset dependency edge. The compiler records the resolved canonical path and content hash in compilation evidence so a future incremental host can invalidate on content change. M1b need not build a general incremental compiler.
- The asset contains exactly one `$value`, as M0b already requires. Multiple exports, named roots, globbing, and transitive asset imports are deferred.
- Diagnostics preserve the importing expression span for resolution/expected-type errors and retain the asset-relative parser/TSON span plus asset path for asset errors. No partial value is bound.

### Expected nominal type and identity

The expected `RecordTypeSymbol` or `EnumTypeSymbol` receives an exchange identity from the compilation unit's explicit `$schema` directive. The asset root must be `TsonRecord` or `TsonEnum` with exactly that identity. The asset's reachable definitions must match the program declaration graph by kind, field/case/payload names, declaration order, child type, and stable identity. Same shape with a different identity is rejected.

M1b then translates the validated root recursively into existing bound literals, `BoundRecordConstructionExpression`, and `BoundEnumValueExpression`. This makes evaluation and backend construction use established language paths. The intrinsic disappears before Cope MIR. No asset path, schema catalog object, `TsonValue`, or compiler frontend type enters MIR or generated code.

The generated initialization is deterministic construction at the use site. M1b does not promise singleton identity, eager module initialization, global publication, deduplication, or backend hoisting; Copeland record equality is not installed, so such a promise is unnecessary. A later optimization may share a value only if observational equivalence is proven.

## Stable schema identity for compiled declarations

### Selected initial rule

An asset-participating compilation unit declares exactly one literal directive:

```ts
const $schema: string = "copeland://authority/package/module";
```

It is compiler metadata and is not a normal global variable, emitted field, function-visible binding, package version, assembly attribute, or JavaScript export. The value must satisfy M0b's schema identity grammar: a nonempty `copeland://` identity with no whitespace or `#`. The compiler derives:

```text
record or enum     schema#Type
record field       schema#Type.field
enum case          schema#Enum.Case
enum payload       schema#Enum.Case.payload
```

The directive is explicit because current package, module, and project identities do not exist as stable language concepts. The authority/path is authored exchange policy. Reproducible builds use the literal unchanged on both backends. A rename changes identity and is a deliberate compatibility break in this first rule. Aliases, rename maps, schema negotiation, and version migration are deferred.

### Candidate evaluation

| Candidate | Stability and parity | Decision |
| --- | --- | --- |
| Package identity + module path | Attractive later, but neither package nor module identity is implemented | Deferred. |
| Explicit source schema directive | Reproducible, backend-neutral, visible to review, already uses M0b identity law | Selected. |
| Project configuration | Can be stable, but hides exchange identity outside source and no project model exists | Deferred. |
| CLR assembly/namespace/type | C#-specific and refactor/toolchain sensitive | Rejected. |
| JavaScript/npm package | Cannot stand in for CLR or source metadata | Rejected. |
| Generated content hash | Stable only while structure is identical and makes intentional identity/version policy opaque | Rejected. |
| External catalog identity | Useful for later exchange integration but adds catalog/project composition | Deferred. |
| `RecordTypeId`, `MirRecordTypeId`, backend names | Allocation-local or backend-private | Rejected. |

## Runtime value projection design

Runtime projection is designed here but not selected for M1b.

| Source type | Eligibility | Projector inputs/order | Failures and parity |
| --- | --- | --- | --- |
| `boolean` | Supported when reached from an eligible root | Emit `true`/`false` | Only configured output/resource limit. |
| `number` | Supported | Read binary64 bits; normalize all NaNs; retain `-0` and infinities; emit 16 uppercase hex digits | Writer limit; identical bit spelling on both backends. |
| `string` | Supported if it is a valid Unicode scalar sequence | Escape by M0b rules; do not use host JSON | Invalid generated string state is an invariant failure; output limit is ordinary failure. |
| structural object | Not a source runtime type | None | Remains document-only. |
| nominal record | Supported after stable identity | Generated projector reads fields by declaration-known C# members or private JS Symbols, in declaration order | No field discovery; output limit is ordinary failure; invalid carrier is terminal invariant failure. |
| payload enum | Supported after stable identity | Generated closed case dispatch, then declaration-ordered payload projection | Unknown/private malformed case is terminal invariant failure; output limit is ordinary failure. |

Nested projection is statically generated over the closed reachable type graph. Unsupported arrays, Results, tables, or optionality are compile-time diagnostics. No projector accepts `object`, `dynamic`, dictionary, arbitrary CLR instance, JavaScript object, `unknown`, or `any`.

The first runtime API, when implemented, should be explicitly named text encoding, for example:

```ts
const encoded: string ! TsonEncodeError = tsonEncodeText(value);
```

This is a compiler intrinsic lowered to generated code because projector selection depends on the static source type. It returns `Result` because a configured maximum output/depth can be exceeded. `tsonEncodeUtf8` is a separate future byte operation; text and bytes are not aliases. No public `TsonValue` is exposed. Internally, generated type-specific projectors should call a small demand-emitted TSON writer rather than construct a runtime DOM or concatenate ad hoc complete strings.

## Candidate runtime encoding path

```text
statically eligible compiled value
  -> generated type-specific projector
     -> declaration-known record slots / closed enum case dispatch
  -> demand-emitted backend-private canonical writer
  -> canonical Unicode string
  -> optional later strict BOM-free UTF-8 encoding
```

The writer is mechanism, not a universal serialization framework. Its semantic contract is the M0b canonical format. It has only closed operations needed for schema declarations, primitive leaves, record fields, enum payloads, indentation, escaping, and bounds.

## Eventual runtime decoding boundary

```text
canonical text or bytes
  -> one cross-backend-conformant TSON parser       [unsolved]
  -> private semantic value or parser event stream
  -> generated expected-schema validator
  -> generated closed nominal constructor
  -> complete compiled value or typed TsonDecodeError
```

Parsing failure and type failure are different stages. Syntax includes malformed tokens/envelope; canonicality means valid TSON whose spelling is not canonical for a canonical-only API. Schema validation then checks expected target identity, exact record fields, enum case and payloads, nested values, and limits.

No partial record/enum is published. Generated validation first proves the complete subtree, then invokes the legitimate backend constructor. C# generated code is in the same generated assembly and can call internal complete constructors. JavaScript decoder code is emitted into the same closure as private type/field Symbols and constructor functions. Neither uses reflection, public setters, object enumeration, `Activator`, prototype lookup, or counterfeit host shapes.

If runtime decode syntax is later selected, generic `decode<T>` is not currently viable. Contextual expected typing is the compatible direction:

```ts
const decoded: Settings ! TsonDecodeError = tsonDecodeText(text);
```

The success component gives the expected schema. This API is not authorized until parser availability is solved for both C# and JavaScript.

## Illustrative generated C# shape

The following is design illustration, not current output or M1b implementation:

```csharp
internal static class __Tson_Settings
{
    internal const string Identity = "copeland://example/app/settings#Settings";

    internal static bool TryWrite(
        __TsonWriter writer,
        __CopeRecord_r0 value,
        out TsonEncodeError error)
    {
        if (!writer.BeginRecord(Identity))
        {
            error = TsonEncodeError.ResourceLimit();
            return false;
        }

        if (!writer.WriteField("enabled", value.__cope_record_field_r0_f0)
            || !writer.WriteNumberField(
                "retryCount",
                global::System.BitConverter.DoubleToUInt64Bits(
                    value.__cope_record_field_r0_f1)))
        {
            error = TsonEncodeError.ResourceLimit();
            return false;
        }

        writer.EndRecord();
        error = default;
        return true;
    }

    internal static __CopeRecord_r0 ConstructValidated(
        bool enabled,
        double retryCount)
    {
        return new __CopeRecord_r0(enabled, retryCount);
    }
}

internal static bool TryWriteState(
    __TsonWriter writer,
    State value,
    out TsonEncodeError error)
{
    switch (value)
    {
        case State.Active active:
            return writer.WriteEnumCase(
                "copeland://example/app/settings#State",
                "Active",
                active.Since,
                out error);
        case State.Disabled:
            return writer.WriteEnumCase(
                "copeland://example/app/settings#State",
                "Disabled",
                out error);
        default:
            throw new InvalidOperationException(
                "Copeland C# backend invariant failure.");
    }
}
```

The actual current record carrier is a sealed class with an internal constructor and internal fields; payload cases are sealed nested records. Generated projector/decoder code therefore has legitimate direct access. A small writer is preferred to runtime TSON nodes because the user API needs canonical text, the schema graph is statically closed, and a DOM would add allocations and a second public model. It is preferred to monolithic direct string emission because one bounded writer can own escaping, indentation, limits, and binary64 spelling consistently.

## Illustrative generated JavaScript shape

The corresponding design remains inside the backend's generated closure:

```javascript
const __settingsIdentity = "copeland://example/app/settings#Settings";

function __writeSettings(writer, value) {
    __requireSettings(value);
    writer.beginRecord(__settingsIdentity);
    writer.writeBooleanField("enabled", value[__settingsEnabled]);
    writer.writeNumberBitsField(
        "retryCount",
        __binary64Bits(value[__settingsRetryCount]));
    writer.endRecord();
}

function __constructValidatedSettings(enabled, retryCount) {
    return __makeSettings(enabled, retryCount);
}

function __writeState(writer, value) {
    __requireState(value);
    switch (value.$tag) {
        case "Active":
            writer.writeEnumCase(
                "copeland://example/app/settings#State",
                "Active",
                [value.$payload[0]]);
            return;
        case "Disabled":
            writer.writeEnumCase(
                "copeland://example/app/settings#State",
                "Disabled",
                []);
            return;
        default:
            __panic();
    }
}
```

`__settingsEnabled`, `__settingsRetryCount`, `__makeSettings`, enum type tokens, and validators are closure-private backend names. Projectors gain access by being emitted in the same closure, not by making Symbols public. They never enumerate properties, depend on prototypes, traverse arbitrary objects, or call `JSON.stringify`. A `DataView` over a fixed eight-byte buffer is the likely JavaScript binary64-bit mechanism; parity tests, not host number-to-string formatting, must ratify it. Constructed records remain null-prototype, non-enumerable symbol-slotted, and frozen.

## Cross-backend canonical parity

The contract is **both exact Unicode scalar/string contents and exact BOM-free UTF-8 bytes**. Text APIs must produce the exact canonical string. A later UTF-8 API must encode that string strictly, without BOM, with no replacement of invalid scalar input. M0b's printer and byte-idempotence tests are the oracle until a separately extracted format contract is justified.

Parity includes:

- identical embedded schema identity and reachable schema declarations;
- LF only, final LF, four-space indentation, and no BOM;
- identical declaration, field, case, and payload order;
- identical string escaping, including controls, quotes, reverse solidus, U+2028/U+2029, supplementary scalars, and rejection of isolated surrogates;
- binary64 as uppercase 16-digit bits, retaining `-0`, infinities, and canonicalizing every NaN to `7FF8000000000000`;
- identical semantic rejection category even where diagnostic prose or source position representation differs.

Adversarial proof cases must include `-0`, multiple NaN payloads, both infinities, empty string/object/record/payloads, escape-sensitive ASCII controls, BMP and supplementary Unicode, same-shaped distinct records/enums, nested records/enums, malformed/mismatched identities, wrong case, missing/extra/reordered fields and payloads, and every exact resource boundary plus one-over cases.

## Error model

M1b needs compiler diagnostics, not runtime errors:

- asset resolution/read/extension/project-root failure;
- ordinary parser diagnostics from the asset;
- existing `COPE-TSON-0001` through `COPE-TSON-0005` restriction, schema, value, limit, and canonicality diagnostics;
- missing/invalid source `$schema` identity;
- unsupported expected source type;
- root identity or reachable schema mismatch.

These diagnostics must point to the import site or asset span as appropriate and publish no bound value.

For future runtime APIs, keep two small payload enums rather than one universal taxonomy:

```text
TsonEncodeError = ResourceLimit(kind)

TsonDecodeError = Syntax(position)
                | Canonicality(position)
                | Schema(expectedIdentity, actualIdentity, path)
                | ResourceLimit(kind)
```

`Schema` covers target-type, exact record-field, enum-case, and enum-payload mismatch through bounded detail/path data; separate public variants for every validator branch are unnecessary initially. Runtime data failures return `T ! E`. Unsupported projection type and missing schema identity are compile-time diagnostics. Invalid private backend carriers or an impossible generated constructor branch are terminal backend invariant failures. Host exceptions do not become ordinary TSON control flow and `try`/`except` does not catch them.

## Structural object decision

`TsonObject` is implemented for compiler-host documents, but Copeland has no general immutable structural-object runtime type. `ObjectLiteralExpressionSyntax` is accepted only when contextually constructing a nominal record; the binder reports `COPE-REC-0005` without expected record context and resolves member access only through nominal record fields. `CL-OBJECT-001` deliberately excludes general objects.

Therefore structural objects remain **TSON-document-only**. M1b rejects a `TsonObject` root or nested object when projecting to a compiled value. A separate language milestone would have to define a structural runtime type, exact field typing/order, C# representation, JavaScript null-prototype representation, equality, and access before runtime structural-object projection can graduate. Arbitrary JavaScript or CLR objects never substitute for that milestone.

## Deferred-type graduation criteria

| Type family | Graduation evidence required |
| --- | --- |
| Arrays | Add an explicit `TsonArray` variant and element law, canonical text, bounds, both-profile parsing, and cross-backend projection. Arrays cannot be encoded as numeric-key objects. |
| Results | Define canonical success/error identity distinct from payload enum/object; prove nested error/success types and no ambiguity with ordinary enums. |
| Tables | First approve array and Result TSON variants, then define a typed ordered column-object exchange law independent of private table carriers. No direct table-to-JSON path. |
| Optionality | Define a null-less explicit absence/value law, likely ordinary tagged data only after its standard type/generic ownership exists. |

No full syntax is designed here.

## JSON boundary

JSON remains a future compatibility lowering:

```text
compiled value
  -> TSON projection
  -> explicit JSON compatibility lowering
```

and:

```text
JSON
  -> validated untyped JSON
  -> schema-directed TSON
  -> compiled nominal value
```

Host `System.Text.Json`, `JSON.stringify`, property enumeration, CLR reflection, JavaScript prototypes, and host numeric formatting do not define TSON. Payload-enum JSON tagging remains deferred.

## Project and dependency ownership

### Current graph

```text
Copeland.Cli
  -> Copeland.TS -----------------> Copeland.TS.Mir
       |  Syntax + parser                 ^
       |  Binder + lowering               |
       `- Copeland.TS.Tson                |
  -> Copeland.TS.Backend.CSharp ----------'
  -> Copeland.TS.Backend.JavaScript ------'

generated C# / generated JavaScript
  -X-> Copeland.TS
  -X-> Copeland.TS.Tson
```

### M1b graph

```text
path-aware compiler host
  -> Copeland.TS.Syntax + Copeland.TS.Tson
  -> expected-type asset projection in Copeland.TS
  -> existing Copeland.TS.Mir expressions
  -> existing backend-private constructors

generated application
  -> ordinary generated value only
```

Retain colocation for M1b. Asset ingestion already occurs in the frontend, so no new consumer needs a separate syntax or semantic-TSON assembly. Extracting `Copeland.TS.Syntax` would not help JavaScript runtime parsing. Extracting semantic TSON contracts or adding a runtime-neutral project is justified only when a concrete runtime encoding/decoding implementation needs an assembly boundary and has a JavaScript ownership answer. Backends must not reference the frontend; MIR must not reference `TsonValue`; generated apps must not reference compiler assemblies.

A future runtime encoding slice should first use demand-emitted backend-private helpers. Promote a shared .NET runtime package only after repeated generated helpers prove a stable dependency and after JavaScript parity is explicitly owned. Do not create a universal compiler/serialization SDK or target-pack architecture.

## Decision matrix

| Item | Classification | Reason |
| --- | --- | --- |
| Compile-time `.obj.ts` asset ingestion | implement in M1b | Existing parser/profile works; require embedded identity and one nominal root. |
| Compile-time `.tson` asset ingestion | implement in M1b | Existing canonical profile and byte check work; no runtime parser. |
| Runtime record encoding | design accepted but deferred | Generated field-known projector plus closed writer is viable after M1b. |
| Runtime enum encoding | design accepted but deferred | Generated closed case dispatch and ordered payloads are viable. |
| Runtime structural-object encoding | rejected | No source runtime structural-object type exists. |
| Runtime TSON text decoding | unresolved blocker | No parser available on both runtimes without a second grammar. |
| Runtime TSON bytes | design accepted but deferred | Must be a separate strict BOM-free UTF-8 API after text projection. |
| Exposed `TsonValue` in user code | rejected | Compiler-host class is not a cross-backend runtime contract. |
| Generated projectors | design accepted but deferred | Correct reflection-free runtime encoding mechanism. |
| Shared runtime library | design accepted but deferred | No concrete M1b runtime dependency; packaging and JS ownership remain premature. |
| Existing compiler parser in runtime C# | rejected | Pulls compiler boundary into apps and does not solve JS. |
| Parser availability in JavaScript | unresolved blocker | No portable/generated parser architecture exists. |
| Explicit `$schema` identity rule | implement in M1b | Smallest reproducible backend-neutral identity source. |
| Reflection or dynamic discovery | rejected | Violates closed nominal and NativeAOT posture. |
| Arbitrary CLR/JavaScript host objects | rejected | No host traversal or structural counterfeit path. |
| Arrays | design accepted but deferred | Requires a TSON array variant and law. |
| Results | design accepted but deferred | Requires distinct canonical success/error law. |
| Tables | design accepted but deferred | Requires arrays and Results; private carriers are not exchange data. |
| Optionality | design accepted but deferred | Requires explicit null-less absence/value law. |
| JSON | design accepted but deferred | Compatibility lowering only after applicable TSON projection laws. |
| Dedicated runtime TSON parser | rejected | Would violate the one-parser doctrine. |
| Import-based asset syntax | rejected for M1b | Module/import/export semantics are unresolved. |

## Exact CTS-TSON-M1b scope

Implement one vertical slice and nothing else:

1. Recognize one explicit literal compilation-unit `$schema` directive and derive stable identities for ordinary record/enum declarations by the M0b format.
2. Recognize the compiler-only `tsonAsset("relative-path.obj.ts|relative-path.tson")` expression only in an explicitly annotated local `const` whose expected type is a nominal record or payload enum.
3. Add path-aware compiler-host input sufficient to resolve the literal asset relative to the primary source file within a declared project root; pass the existing CLI source path through that context without adding a command or module system.
4. Require one self-described asset root. Use the existing `ObjectTypeScript` profile for `.obj.ts` and `CanonicalTson` for `.tson`; require embedded `$schema` in both.
5. Validate exact root exchange identity and reachable record/enum schema equivalence against the expected compiled type. Support only Boolean, Number, String, Record, and Enum in that reachable graph.
6. Translate the validated root into existing bound literal, record-construction, and enum-value expressions. Let existing lowering, shared MIR validation, C# constructors, and JavaScript private constructors do the rest. Add no TSON MIR node or backend/frontend dependency.
7. Prove one record-root and one payload-enum-root asset on both backends, including nested values, same-shaped nominal rejection, deterministic diagnostics/spans, canonical `.tson` rejection, `.obj.ts` comments/layout, path escape/missing-file cases, repeated output, browser-compatible standalone JavaScript, and ordinary-C#/NativeAOT-compatible generated shape. A NativeAOT publish proof is required only if M1b changes the claimed posture or introduces a runtime dependency; the recommended slice introduces none.
8. Add no runtime encode/decode API, parser, `TsonValue` exposure, bytes, arrays, Results, tables, optionality, structural runtime objects, imports, packages, shared runtime library, reflection, host traversal, or JSON behavior.

## Bounded CTS-TSON-M1b recommendation

Implement compile-time `.obj.ts`/`.tson` ingestion through explicit `$schema` identity and the typed `tsonAsset` compiler intrinsic, lowering one validated nominal root to existing record/enum construction paths for C# and JavaScript with no runtime parser, runtime TSON model, reflection, or new dependency boundary.
