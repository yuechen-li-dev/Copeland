# Copeland TS TSON shared parser and semantic model (CTS-TSON-M0b)

**Status:** implemented foundation at revision lineage beginning from `0733e2a50af16e369a50d02f5f0d6c420abb40d6`.

## Outcome and corrected architecture

TSON is a restricted semantic projection of Copeland TypeScript syntax, not a second frontend:

```text
*.obj.ts or *.tson
  -> SyntaxTree.Parse(string)
  -> CompilationUnitSyntax
  -> TSON restriction, catalog, and value validation
  -> immutable TsonValue
  -> canonical restricted Copeland syntax
```

M0a's parallel grammar and `TsonParser` recommendation is superseded. Production contains one `Lexer`, one `Parser`, one `SyntaxKind`, and one syntax-node family under `Copeland.TS.Syntax`. `TsonDocumentReader.ReadSelfDescribed` invokes the public production `SyntaxTree.Parse(source)` entry point for both profiles. Ordinary lexer/parser diagnostics retain their `COPE-LEX-*` or `COPE-PARSE-*` code and source span. TSON diagnostics begin at resource preflight or semantic restriction; no ordinary syntax diagnostic is translated.

## Selected topology

M0b uses Option C: the backend-neutral `Copeland.TS.Tson` namespace is isolated under the existing `Copeland.TS` assembly. No syntax extraction occurred.

The parser and all syntax contracts are already public, so colocation required no `InternalsVisibleTo`, adapter duplication, project reference, or cycle. A new standalone project could not reuse the parser without depending on the complete frontend assembly. Extracting `Syntax` would move widely used public types for only one new consumer. Extraction becomes appropriate when a second assembly-level consumer needs syntax without binding/lowering and the move can preserve public identities and frontend tests.

TSON has no dependency on MIR APIs, either backend, CLI, Machina, Aurelian, Dominatus, Roslyn, reflection, or a host serializer. It does not appear in solution/project topology as a new package.

## Profiles and envelope

Both profiles use the same grammar and value meanings.

`ObjectTypeScript` accepts ordinary parser trivia and layout. It accepts either an embedded schema identity or an explicit `authoringSchemaIdentity` supplied through `DecodeAuthoringValue`. A contextual root annotation may direct an ordinary object literal into a nominal record.

`CanonicalTson` requires a self-contained `$schema` binding, accepts no ambient identity, and verifies exact canonical bytes. The printer emits LF newlines, four-space indentation, no comments, ordinal declaration ordering, declaration-ordered record fields and enum payload arguments, authored-order structural fields, and one final newline. `PrintUtf8` returns UTF-8 without a byte-order mark, and repository attributes retain LF for checked-in `.tson` fixtures.

The restricted document envelope is ordinary Copeland syntax:

```ts
const $schema: string = "copeland://example/people";

record User {
    name: string;
    role: Role;
}

enum Role {
    Admin,
    Named(label: string),
}

const $value = $record.User({
    "name": "Ada",
    "role": Role.Admin,
});
```

Exactly one `const $value` is required. `$schema`, record declarations, enum declarations, and `$value` are the only top-level forms. `$record.Type({...})` is a reserved call-shaped canonical data constructor used where contextual record typing is unavailable. `Enum.Case` and `Enum.Case(...)` are the existing enum construction syntax. `$number("HHHHHHHHHHHHHHHH")` is a reserved call-shaped data leaf containing binary64 bits. No call is evaluated; every other call is rejected.

## Stable identity

The schema identity is an absolute, whitespace-free `copeland://...` string without `#`. Derived identities use declared names and are independent of traversal order:

```text
type       = schema#Type
field      = schema#Type.field
enum case  = schema#Enum.Case
payload    = schema#Enum.Case.payload
```

Canonical text retains `$schema` and all declarations, so these identities reconstruct without project state. Same-shaped records and enums remain distinct; equal case names in different enums remain distinct. Compiler-local `r0`, `r0.f0`, MIR names, and backend symbols never enter TSON.

M0b deliberately does not implement versions, aliases, registries, package resolution, or rename migration.

## Semantic and schema model

`TsonValue` is a closed, non-subclassable-outside-the-assembly hierarchy:

```text
TsonBoolean
TsonNumber
TsonString
TsonObject
TsonRecord
TsonEnum
```

Collections are copied and published read-only. Duplicate fields and missing nominal field identities are rejected by constructors. Public semantic nodes retain reference equality; M0b installs no public TSON equality or hashing law.

`TsonCatalog` contains one schema identity and immutable ordered `TsonRecordDefinition`/`TsonEnumDefinition` values. Field and payload types are limited to Boolean, Number, String, Object (`$object` in source), Record reference, and Enum reference. Projection validates exact record fields, declaration ordering, child types, enum/case ownership, arity, and payload types. M0b rejects every nominal schema cycle. This is stricter than merely rejecting cyclic values and keeps the first slice finite without references or lazy construction.

Structural objects retain authored order and string field names. They have no field identities. Records are inspectably distinct and are normalized into schema declaration order. Enum semantic payloads regain declaration names and identities even though established source construction is positional.

## Numbers and strings

`TsonNumber` stores normalized binary64 bits. Negative zero and both infinities are preserved. Every NaN input becomes `0x7FF8000000000000`. Canonical text uses an uppercase 16-digit bit spelling inside `$number`, which the ordinary parser accepts as a string argument. This avoids changing its intentionally narrow integer lexer and covers every binary64 value exactly.

Authoring may use ordinary integer literals and unary minus, including `-0`. Arbitrary arithmetic is rejected.

TSON decodes a restricted deterministic escape set after ordinary parsing: quote, apostrophe, reverse solidus, `b`, `f`, `n`, `r`, `t`, and four-digit `u` escapes. Isolated UTF-16 surrogates are rejected; valid pairs are retained. Canonical output uses double quotes, required control escapes, uppercase `u` escapes, and raw valid Unicode scalars.

## Restriction and diagnostic inventory

The pass accepts only envelope metadata, schema declarations, root binding, primitive leaves, object literals, contextual or reserved record construction, and enum construction. Functions, arbitrary calls/names, mutable bindings, assignment, control flow, arrays, Results, tables, `null`, `undefined`, binary computation, member access outside enum construction, `with`, match, try/except, indexing, propagation, and unwrap cannot become TSON values. Syntax absent from the current Copeland grammar continues to receive ordinary parser diagnostics; the parser is not weakened or given TSON modes.

| Code | Family |
| --- | --- |
| `COPE-TSON-0001` | document profile and envelope |
| `COPE-TSON-0002` | executable or unsupported syntax |
| `COPE-TSON-0003` | schema, identity, catalog, or type grammar |
| `COPE-TSON-0004` | value, field, case, payload, string, or type validation |
| `COPE-TSON-0005` | resource limit or canonical-byte validation |

Source-caused diagnostics have deterministic nonempty spans. Host exception text is not used as semantic output, and no partial document is returned.

## Resource bounds

Defaults are maximum source length 1,048,576 UTF-16 code units; lexical/semantic nesting 64; declarations 256; fields per aggregate 256; enum cases 256; payloads per case 64; value nodes 100,000; and string length 262,144 UTF-16 code units. `TsonLimits` permits lower caller-selected positive bounds.

Source length is checked before lexing. Delimiter nesting is preflighted with the production `Lexer`, not a TSON lexer. Remaining limits are enforced while building catalog/value state. Representative boundary failures are tested as `COPE-TSON-0005`. This is a bounded foundation, not a complete hostile-input security specification.

## Round-trip evidence and exclusions

Focused tests prove:

```text
.obj.ts -> production parser -> TsonValue -> canonical text
        -> production parser -> equivalent TsonValue

parseCanonical(printCanonical(value)) ~= value
printCanonical(parseCanonical(printCanonical(value))) == identical bytes
```

The equivalence helper is test-only. Filesystem fixtures under `tests/Copeland/Copeland.TS.Tests/Tson` separately own valid `.obj.ts`, valid `.tson`, and invalid `.obj.ts` documents. Topology validation prohibits a TSON lexer/parser/token hierarchy, forbidden dependencies, and TSON array/Result/table/JSON variants.

M0b adds no JSON, array, Result, table, optionality, runtime carrier conversion, MIR/backend integration, CLI behavior, MSBuild/file association, package/version change, imports, execution, reflection, or generic serialization SDK.

## Next milestone

The next milestone should remain a compiler-independent hardening slice: expand malformed-envelope and canonical fixture coverage, decide whether syntax assembly extraction has a second real consumer, and define an explicit catalog-directed decode API for externally constructed catalogs if needed. Table or JSON integration must wait for separately ratified array/Result/table TSON laws. Runtime-carrier conversion and CLI registration remain independent future decisions.
