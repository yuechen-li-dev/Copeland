# Copeland TS JavaScript emission profiles design (CTS-JS-EMIT-M0a)

> **Routing update:** CTS-JS-EMIT-M1 implements the approved Diagnostic/Symbolic profile slice on the M0b scoped allocator. This design remains the vocabulary and measurement authority; Release remains deferred. See [the M1 architecture record](../architecture/copeland-ts-symbolic-javascript-emission-cts-js-emit-m1.md).

**Status:** accepted documentation, architecture-audit, and temporary-experiment design. This milestone changes no production emitter, generated artifact, corpus hash, CLI behavior, dependency, or runtime law. The current `--emit javascript` output remains the Diagnostic authority.

## Decision

Copeland TS should grow three deterministic JavaScript emission profiles over a backend-local structured token/event writer:

```text
Cope MIR
    -> JavaScript semantic emission events, scopes, and binding identities
        -> Diagnostic printer: verbose provenance and stable layout
        -> Symbolic printer: compact Chinese semantic compounds
        -> Release printer: shortest safe scoped names and compact tokens
```

The recommended structure is smaller than a complete JavaScript AST. It records scopes, declarations and references to the same binding, literal/token boundaries, formatting opportunities, and optional source marks. Existing backend-specific lowering remains backend code. The printers do not reinterpret MIR and do not perform general JavaScript optimization.

Diagnostic remains the checked-in corpus authority and the default for `copeland compile ... --emit javascript`. Symbolic should receive a separate checked-in corpus only when CTS-JS-EMIT-M1 implements it. Release should be deterministic and tested for parity, but should normally be a packaging artifact rather than a repository-wide hash-pinned corpus. No profile migration begins until CTS-TSON-TABLE-M2/M3 close and one explicitly approved regeneration changes JavaScript artifacts.

## Evidence audited

The audit read the canonical language profile; the CTS-M1, M2, M3, M4c, M6c, REC-M2, TABLE-M2, TSON-M2b, TSON-ARRAY-M1, and TSON-TABLE-M1 JavaScript architecture and migration records; all six files in `Copeland.TS.Backend.JavaScript`; the CLI; backend corpus/hash owners; representative generated artifacts; JavaScript and parity runtime tests; and frontend/MIR source-span models.

The current implementation is one 3,069-line `JavaScriptBackend` plus:

- `JavaScriptTextWriter`, which writes four-space-indented lines into one `StringBuilder`;
- `JavaScriptLiteralWriter`, which owns invariant numbers and explicit UTF-16 string escaping;
- `JavaScriptIdentifierEncoder`, which preserves a conservative ASCII identifier subset and hex-encodes other user names;
- `JavaScriptCompilation`, which returns only source text or backend-local diagnostics.

`JavaScriptBackend.Emit` validates MIR, builds enum/record/table/Result catalogs, allocates globally unique `__cope_m3_<purpose>_<ordinal>` names after reserving emitted user names, and interpolates complete JavaScript lines and expressions. `EmittedExpression` carries ordered prelude statements for exactly-once and control-flow staging. There is no JavaScript token model, lexical scope tree, binding identity, profile option, compact printer, mapping sink, module/export model, or source map.

Demand emission already exists. The value runtime appears only for enum, record, table, Result, unwrap, handler, or TSON needs. Flow helpers appear only for `try`/`except`; array TSON helpers appear only for array plans; TSON writers are enclosed in one demanded runtime closure. The demand decision is module-local: separate standalone artifacts repeat the demanded family.

## Measured artifact inventory

Measurements use UTF-8 without BOM, Node v26.2.0, zlib 1.3.1 gzip level 9, and Brotli 1.2.0 quality 11. “Obvious scaffolding” is a conservative byte boundary from the start of the generated artifact to the first user-authored function declaration. It therefore excludes generated staging and validation inside user functions and must not be treated as an exact semantic attribution.

| Case | Source bytes | Diagnostic JS | gzip | Brotli | Obvious scaffold |
| --- | ---: | ---: | ---: | ---: | ---: |
| Primitive `main-returns-42` | 195 | 156 | 124 | 110 | 15 (9.6%) |
| Payload enum and match | 457 | 4,536 | 844 | 754 | 3,642 (80.3%) |
| Result propagation | 190 | 1,853 | 603 | 523 | 1,334 (72.0%) |
| Typed `try`/`except` | 198 | 4,651 | 1,020 | 908 | 2,768 (59.5%) |
| Immutable record | 212 | 3,967 | 748 | 654 | 2,821 (71.1%) |
| Record table | 504 | 23,201 | 2,470 | 2,150 | 19,476 (83.9%) |
| Compile-time TSON record asset | 238 | 1,862 | 567 | 488 | 1,633 (87.7%) |
| Runtime TSON record encoding | 370 | 14,918 | 2,590 | 2,321 | 14,439 (96.8%) |
| Runtime TSON array encoding | 542 | 25,224 | 3,292 | 2,878 | 23,813 (94.4%) |
| CTS-TSON-TABLE-M1 representative | 971 | 38,279 | 3,327 | 2,762 | 33,243 (86.8%) |

Raw source size is not transfer size. The required artifact compresses to 8.7% of raw with gzip and 7.2% with Brotli because long names, descriptors, validators, and tags repeat. Neither compressed number measures parse/compile time, peak memory, runtime performance, cache behavior, or object-shape quality.

## Where the current size comes from

The 38,279-byte CTS-TSON-TABLE-M1 artifact is the most useful detailed case. The measurements below overlap and must not be added together.

| Source | Evidence in the required artifact | Interpretation |
| --- | --- | --- |
| Long lexical bindings | 528 generated-name occurrences, 106 distinct names, 14,915 occurrence bytes (39.0% raw) | Provenance-rich spellings are the largest directly renameable raw source component. Compression already amortizes them heavily. |
| Validation | Top-level validation functions total 14,812 bytes | Enum, Result, record, column, table, and row invariants are correctness scaffolding, not formatting noise. |
| Repeated Result validators | Eight near-isomorphic Result validators total 7,728 bytes | Type-specialized validation creates a real within-module helper-family duplication target. |
| Construction and closed initialization | Constructor/create functions total 15,218 bytes; the two table-create functions are 10,616 and 2,510 bytes | This includes closed constants, frozen storage, column closures, descriptor assembly, and table publication. Naming alone cannot remove it. |
| Property descriptors | The exact `writable: false, enumerable: false, configurable: false` phrase occurs 41 times and occupies 2,255 raw bytes | A static descriptor helper or a different safe construction scheme may win, but call overhead and object-shape behavior require measurement. |
| Private `Symbol` descriptions | 27 descriptions; replacing `Symbol("...")` with `Symbol()` saves 282 raw, 93 gzip, and 58 Brotli bytes | Descriptions are a small raw win and a smaller transfer win. They are useful in Diagnostic debugging. |
| Whitespace and formatting | Removing blank lines and leading indentation only saves 3,528 raw, 86 gzip, and 57 Brotli bytes | Compact formatting matters mainly for raw/parse input, not compressed transfer in this artifact. |
| Repeated strings | 179 string-literal occurrences, 50 distinct; repeated occurrences beyond the first represent 978 literal bytes | Tags and type-test strings compress well. Pooling is justified only when declaration/reference overhead produces a measured net win. |
| Runtime scaffolding | 33,243 bytes precede the first user function | The artifact is runtime-heavy even before generated checks inside user functions are counted. |
| User-program region | 5,036 bytes from the first user function to EOF | This includes actual program logic plus generated staging, validators, Results, matches, and bounds flow. It is not all user-authored logic. |

The backend already emits one shared `make` helper and one column carrier family per artifact. It nevertheless emits one validator for every structural Result type, separate validators and constructors for every nominal family, and separate table create/validate families. Every standalone compilation unit repeats its own demanded panic, carrier, validator, and writer prelude. TSON plan record/enum/array functions are also specialized, while primitive writers are shared inside the demanded TSON closure.

## What may be renamed

“Translate” means replace an internal compiler concept with a stable Symbolic semantic compound. “Mangle” means allocate a non-semantic short binding. A source map or name manifest supplements observability; it never authorizes changing serialized or public meaning.

| Category | Diagnostic | Symbolic | Release | Law |
| --- | --- | --- | --- | --- |
| Internal lexical bindings | Preserve verbose provenance | Translate | Mangle | Always eligible once the allocator proves scope and collision safety. |
| Private compiler-generated `Symbol` bindings | Preserve verbose binding | Translate | Mangle | The lexical binding is private; the symbol value remains unique. |
| Debug-only `Symbol` descriptions | Preserve by default | Preserve or compact by option | Remove | Descriptions aid debugging but do not define nominal identity. |
| Generated helper functions | Preserve semantic name | Translate | Mangle or eliminate if dead | Helper behavior and evaluation order remain unchanged. |
| Public/exported Copeland API names | Preserve | Preserve | Preserve | No current export MIR exists; future exports must be explicitly marked and excluded. |
| Top-level user functions used by a host | Preserve | Preserve | Preserve until closed-world entry metadata exists | Current Node harnesses append calls such as `main()`; Release cannot silently break that observable boundary. |
| Other user-authored bindings | Preserve through current encoding | Preserve by default | Scope-mangle only with an approved closed-world contract | User names are debugger-observable even without exports. |
| Stable TSON schema identities | Preserve | Preserve | Preserve | Serialized identity and canonical TSON are immutable data, never minifier vocabulary. |
| Record field names and enum case/payload names in TSON | Preserve | Preserve | Preserve | Canonical text and schema meaning are externally observable. |
| Private record field `Symbol` bindings | Preserve verbose binding | Translate | Mangle | Their descriptions are separately governed debug metadata. |
| Textual private enum/Result tags | Preserve | Initially preserve | May compact only as one closed representation change | `$tag` values are backend-private today, but tests and hostile reflection can observe them; change requires a parity/security audit. |
| Error and diagnostic text | Preserve | Preserve | Preserve unless explicitly classified debug-only | Invariant and unwrap messages have diagnostic value; user-visible diagnostics are never minified. |
| Property names accessed by external JavaScript | Preserve | Preserve | Preserve | Interop/ABI names are excluded from all mangling. |
| Private symbol-keyed properties | Preserve lexical provenance | Translate | Mangle binding; never weaken symbol identity | Reflection may see descriptions, not lexical binding names. |
| Compact-name provenance | In names | In semantic names plus optional map | Source map/name manifest | Maps supplement, rather than replace, stable runtime/serialized text. |

The current `$type`, `$tag`, `$payload`, `$flow`, `$kind`, `$value`, `$handler`, and `$error` keys are compiler-private representation details, not public Copeland APIs. M1 should leave them unchanged to isolate identifier-profile risk. A later Release representation audit may shorten or symbol-key them only if all accesses change atomically and Node/browser/security/shape parity passes.

## Output profile contracts

### Diagnostic

- Preserve current verbose English semantic/provenance names and current `JavaScriptIdentifierEncoder` behavior.
- Preserve stable four-space indentation, LF line breaks, semicolons, literal escaping, and useful `Symbol` descriptions.
- Remain the best artifact for compiler debugging, corpus diffs, snapshot failures, and forensic provenance.
- Remain deterministic and the exact checked-in corpus/hash authority.
- Preserve current output byte-for-byte through M0b where practical; intentional exceptions require an approved corpus migration.

### Symbolic

- Use a fixed `$`-prefixed Chinese compiler vocabulary and deterministic per-semantic-family ordinals.
- Be a readable machine-oriented symbolic dialect, not arbitrary Unicode minification.
- Keep moderate line breaks and structural indentation; remove redundant English provenance and hex-expanded compiler IDs.
- Preserve public/user/serialized names and private representation properties in the first implementation.
- Execute directly in Node and browsers without a runtime decoder or name table.
- Optionally emit a source map/name manifest, but remain understandable without it to a reader who knows the codebook.

### Release

- Use a scope-aware frequency-informed ASCII allocator, shortest safe token separation, and compact formatting.
- Remove debug-only `Symbol` descriptions and dead internal declarations/helpers.
- Preserve public, external, serialized, diagnostic, and stable identity text.
- Keep invariant enforcement, exactly-once staging, and object-shape laws intact.
- Treat external source maps and compact-to-diagnostic name metadata as the readable provenance layer.
- Guarantee determinism and semantic parity, not hand readability.

The proposed future CLI spelling is:

```text
copeland compile input.ts --emit javascript --javascript-profile diagnostic
copeland compile input.ts --emit javascript --javascript-profile symbolic
copeland compile input.ts --emit javascript --javascript-profile release
```

`--javascript-profile` is deliberately explicit and target-specific. Omitting it continues to mean `diagnostic` until a separately approved migration. With implicit output naming, Diagnostic retains `.g.js`, Symbolic may use `.symbolic.g.js`, and Release may use `.min.js`; `--out` remains authoritative. A source map follows the selected output as `<artifact>.map`. The CLI emits one requested artifact, not three automatic siblings.

## Candidate Chinese semantic codebook

The preferred column is the M0a starting point, not owner-taste ratification. Every entry is a fixed NFC code point or fixed short compound; no printer performs translation by dictionary lookup at runtime.

| Atom | Preferred | English meaning | Current backend concepts | Ambiguity and status |
| --- | --- | --- | --- | --- |
| table | 表 | table | table type, singleton, validator, create | Accepted candidate. |
| row | 行 | row | row type, row view, row read | Accepted candidate. |
| column | 列 | column | column carrier, slot, read | Accepted candidate. |
| record | 录 | record | nominal record token, fields, constructor | “录” can mean record/log; accepted candidate in compounds. |
| enum | 枚 | enum | enum token, instances, validation | Accepted candidate. |
| case | 例 | case/variant | enum case dispatch | Could be confused with example; owner review. |
| payload | 载 | payload | enum/Result payload | Also “carry/load”; accepted candidate in compounds. |
| array | 组 | array/group | ordinary array and TSON array plan | Could imply generic group; owner review against “阵”. |
| Result | 果 | Result value/type | Result token, match, validation | Accepted candidate and intentionally distinct from success. |
| success | 成 | success/ok | Result `ok` arm/value | Accepted candidate. Serialized tag remains `ok`. |
| error | 错 | error | Result `err`, handler error | Accepted candidate. Serialized/private tags are separate. |
| flow | 流 | structured flow | handler/function/value completion carrier | Accepted candidate. |
| function | 函 | function | generated/user function concept | Accepted candidate. |
| function transfer | 函传 | transfer to function return | `FlowToFunction` | Accepted compound. |
| lexical handler | 接 | handler/receiver | lexical `except` handler | Could be read as receive/connect; owner review. |
| handler transfer | 接传 | transfer to lexical handler | `FlowToHandler` | Accepted compound if 接 is ratified. |
| type | 型 | type | type/brand tokens | Accepted candidate. |
| value | 值 | value | staged/result/flow value | Accepted candidate. |
| storage | 存 | storage | closed table column arrays | Accepted candidate. |
| token | 令 | token | private object/symbol token | Could mean command; owner review against “符”. |
| brand/provenance | 印 | brand/provenance | WeakSet provenance and nominal brand | Could mean stamp; owner review. |
| construct | 造 | create/construct | record/table/row/value creation | Accepted candidate. |
| validate/require | 验 | validate/require | invariant validators | Accepted candidate. |
| read/access | 取 | read/access | column/row/field access | Accepted candidate. |
| write/update | 更 | internal update/replace | record replacement, internal assignment | Means change rather than write; owner review against “写”. |
| encode | 编 | encode | TSON writer/plan entry | Also compile/edit; accepted in context. |
| source | 源 | source/origin | source text/provenance | Accepted candidate. |
| schema | 式 | schema/form | TSON schema plan/text | Broad “form”; owner review against “模” or “纲”. |
| identity | 识 | identity | stable schema identity and compiler IDs | Could mean knowledge/identifier; owner review. |
| bounds | 界 | bounds | table/array limits | Accepted candidate. |
| invariant | 律 | invariant/law | compiler invariant | Accepted candidate. |
| terminate/panic | 终 | terminal stop | invariant and unwrap panic helpers | Accepted candidate in `律终`/`解终`. |
| helper | 助 | helper | uncategorized generated helper | Accepted fallback, but use a specific compound when possible. |
| runtime | 运 | runtime | generated runtime prelude | Could mean transport/operate; owner review. |
| string | 串 | string | string type/writer/check | Technical and concise; accepted candidate. |
| number | 数 | number | binary64 type/writer/check | Accepted candidate. |
| Boolean | 布 | Boolean | Boolean type/writer/check | Shorthand for 布尔 may be too terse; owner review against 真. |
| field | 域 | field/slot | record field identity | Could imply domain; owner review against “栏”. |
| ordinal/index | 序 | index/ordinal | indexes and allocation suffix role | Accepted candidate. |
| match | 配 | match/dispatch | enum/Result match | Could imply pairing; owner review. |

Compounds should place the broad carrier before the operation: `表行型` (table-row type), `表列存` (table-column storage), `录域` (record field), `果验` (Result validation), `流接` (flow handler), and `串编` (string encode). Vocabulary atoms are not independently substituted into user or serialized names.

## Actual-name translation sample

These are lexical translations of real bindings in the current 38,279-byte artifact. They demonstrate vocabulary density; the temporary token-aware rewrite is not a production renamer.

| Current Diagnostic binding | Proposed Symbolic binding |
| --- | --- |
| `__cope_m3_panic_0` | `$律终甲` |
| `__cope_m3_panic_unwrap_1` | `$解终甲` |
| `__cope_m3_make_2` | `$值造甲` |
| `__cope_m3_record_type_r1_3` | `$录型甲` |
| `__cope_m3_record_instances_r1_4` | `$录印甲` |
| `__cope_m3_record_make_r1_5` | `$录造甲` |
| `__cope_m3_record_require_r1_6` | `$录验甲` |
| `__cope_m3_record_field___cope_00720031002e00660030_7` | `$录域甲` |
| `__cope_m3_record_field___cope_00720031002e00660031_8` | `$录域乙` |
| `__cope_m3_type_9` | `$枚型甲` |
| `__cope_m3_instances_10` | `$枚印甲` |
| `__cope_m3_validate_11` | `$枚验甲` |
| `__cope_m3_result_type_15` | `$果型甲` |
| `__cope_m3_result_validate_16` | `$果验甲` |
| `__cope_m3_result_type_17` | `$果型乙` |
| `__cope_m3_column_type_31` | `$列型甲` |
| `__cope_m3_column_read_32` | `$列取甲` |
| `__cope_m3_column_require_33` | `$列验甲` |
| `__cope_m3_table_row_table_34` | `$表行表甲` |
| `__cope_m3_table_row_index_35` | `$表行序甲` |
| `__cope_m3_table_type_t1_36` | `$表型甲` |
| `__cope_m3_table_row_type_t1_37` | `$表行型甲` |
| `__cope_m3_table_require_t1_38` | `$表验甲` |
| `__cope_m3_table_row_require_t1_39` | `$表行验甲` |
| `__cope_m3_table_create_t1_40` | `$表造甲` |
| `__cope_m3_table_row_create_t1_41` | `$表行造甲` |
| `__cope_m3_table_value_t1_42` | `$表值甲` |
| `__cope_m3_table_rows_t1_43` | `$表行取甲` |
| `__cope_m3_table_column___cope_00740031002e00630030_44` | `$表列槽甲` |
| `__cope_m3_column_type___cope_00740031002e00630030_45` | `$表列型甲` |
| `__cope_m3_table_storage___cope_00740031002e00630030_46` | `$表列存甲` |
| `__cope_m3_table_column_value___cope_00740031002e00630030_47` | `$表列值甲` |
| `__cope_m3_table_receiver_80` | `$表取值甲` |
| `__cope_m3_table_index_81` | `$表序甲` |
| `__cope_m3_table_row_82` | `$表行果甲` |
| `__cope_m3_row_table_85` | `$行表甲` |
| `__cope_m3_row_field_86` | `$行域甲` |
| `__cope_m3_match_87` | `$配甲` |
| `__cope_m3_match_value_88` | `$配值甲` |
| `__cope_m3_result_match_104` | `$果配甲` |

`印` is preferred over the experiment's initial `$录源`/`$枚源` spelling because the `WeakSet` proves created-instance provenance rather than source provenance. This is one of the terms requiring owner taste approval.

## Identifier and ordinal strategy

All Symbolic bindings begin with `$` followed by a semantic compound and an ordinal. `$` is a visual compiler marker, not a collision guarantee. The scoped allocator still owns uniqueness.

An 80-binding synthetic allocation using the real `表列存` category produced:

| Strategy | Example | After first alphabet | Raw | gzip | Brotli | Decision |
| --- | --- | --- | ---: | ---: | ---: | --- |
| Chinese prefix + ASCII base-54 | `$表列存a` | `$表列存$`, `$表列存_`, `$表列存aa` | 1,776 | 368 | 229 | Smallest Symbolic form; punctuation suffixes and ASCII case are less visually uniform. |
| Heavenly Stems | `$表列存甲` | after 癸: `$表列存甲甲`, `$表列存甲乙` | 2,120 | 446 | 261 | Preferred readability candidate; compact families usually stay within one character. |
| Curated 30-character alphabet | `$表列存甲` | after 冬: `$表列存甲甲` | 2,060 | 473 | 281 | Avoids early multi-character suffixes but mixes stems, branches, directions, and seasons; owner review required. |
| Opaque ASCII | `a` | `$`, `_`, `aa` in the measured allocator | 976 | 322 | 196 | Release only; not a Symbolic dialect. |

Raw UTF-8 makes every Chinese ordinal three bytes versus one byte for an ASCII one-character suffix. Repetition narrows that gap under gzip/Brotli. Heavenly Stems are more distinguishable and semantically intentional for the owner, while ASCII suffixes sort more predictably in byte/code-point tools. Neither Chinese sequence sorts in human ordinal order under ordinary code-point sorting; allocation order, not lexical sort, is authoritative.

The recommended M1 starting point is Heavenly Stems with a bijective base-10 continuation: `甲` through `癸`, then `甲甲`, `甲乙`, and so on. Allocation is per semantic compound within a lexical scope, so most names remain one ordinal character. If owner review rejects the two-character overflow or prefers maximum density, select ASCII base-54 before implementation. Do not silently change the alphabet after corpus adoption.

## Unicode safety law

The living [ECMAScript lexical grammar](https://tc39.es/ecma262/multipage/ecmascript-language-lexical-grammar.html#sec-names-and-keywords) defines identifier start/continuation from Unicode `ID_Start`/`ID_Continue` plus ECMAScript additions such as `$`. [Unicode UAX #31](https://www.unicode.org/reports/tr31/) recommends identifier profiles and explicitly warns about default-ignorable characters; [Unicode UAX #15](https://www.unicode.org/reports/tr15/) defines NFC.

Copeland Symbolic emission adopts a deliberately narrower law:

1. The complete candidate identifier is normalized to NFC, and emission rejects a vocabulary or ordinal entry that changes under NFC.
2. The semantic and ordinal code points come only from a versioned curated table checked into the backend. General user Unicode is never admitted merely because JavaScript accepts it.
3. After the initial `$`, every first semantic code point must have `ID_Start`; every later code point must have `ID_Continue`. ASCII Release allocation uses its own fixed grammar.
4. The curated table contains no combining marks, CJK compatibility ideographs, bidirectional controls, default-ignorable or zero-width characters, variation selectors, unpaired surrogates, private-use characters, or unassigned code points.
5. No two entries are equal after NFC. A review also rejects visually identical or dangerously confusable duplicate entries and unjustified mixed-script compounds.
6. The generated source is UTF-8 without BOM and is re-read and checked for NFC, forbidden controls, and exact code-point membership in validation.
7. Reserved words are excluded even when an escape spelling could produce them. Binding allocation compares normalized final names in ordinal code-point order.

Node v26.2.0 parsed `$表列存a`, `$表列存甲`, and `$表列存一`. It also parsed a combining-mark continuation, a zero-width joiner, and a variation selector; those successful parses demonstrate why ECMAScript legality is not sufficient for this profile. Canonically equivalent `é` and `e` plus combining acute were distinct JavaScript bindings, so NFC alias rejection is required. A CJK compatibility ideograph normalized to a unified ideograph and is forbidden.

Raw emoji failed as a lexical binding because it is not `ID_Start`. Emoji remains legal and sometimes useful in a quoted property name or `Symbol("😀")` description. That does not make it suitable or valid as a compiler-generated lexical identifier.

## Collision policy

The M0b allocator must operate on binding identities, not final strings:

1. Build an explicit scope tree for program, function, block, IIFE/closure, switch arm, and generated helper scopes.
2. Register user-authored, imported, exported, and externally fixed bindings first. Preserve a binding's declaration/reference identity even if its printed name changes.
3. Mark public/exported/host-entry names non-mangleable. Until module and entrypoint laws exist, conservatively treat top-level user functions as externally fixed in Release.
4. Allocate generated bindings afterward from the selected profile, excluding reserved words and every occupied normalized name in the binding's collision domain.
5. Permit safe reuse only in disjoint scopes. Never infer safety from different textual purposes alone.
6. Treat `$` as a style marker only. User-authored `$` names are ordinary occupied names unless a future language milestone reserves a namespace.
7. Give helper imports/module preludes a module-unique internal alias derived through the same allocator, not string concatenation.
8. Isolate generated internals in the existing script scope initially. A private closure or native module scope may be introduced only as a separately tested output-contract change.

Bundling can combine compilation units that were independently collision-free. Release therefore needs either native module scope, a generated private closure per unit, or module-unique helper aliases. A bare global `$` prefix is not sufficient.

## Smallest justified internal structure

| Option | Finding |
| --- | --- |
| A. Direct strings plus allocator/profile writer | Insufficient for reliable compact token separation, nested-scope reuse, declaration/reference identity, and source locations. It would preserve the current interpolation fragility. |
| B. Backend-local token/event writer | **Recommended.** Add scope/binding events, typed token/literal writes, soft/hard breaks, and source marks while retaining backend-specific lowering. |
| C. Complete JavaScript AST and multiple printers | Not justified. The backend emits a closed subset and does not need a general parser, transforms, comments, arbitrary syntax, or round trips. |
| D. Readable JS plus external minifier | Reject as the compiler architecture. It adds a tool/runtime/version dependency, cannot implement the Chinese semantic contract, and weakens deterministic ownership. Remain compatible with optional downstream minifiers. |

The event writer must provide at least:

- `ScopeId` and `BindingId` creation, declaration, reference, and non-mangleable flags;
- keywords, punctuators, identifiers, numeric literals, and strings as typed writes;
- automatic required token separation (`return` plus expression, `+` beside `+`, identifier adjacency, comment hazards);
- existing `JavaScriptLiteralWriter` semantics or an equivalent single literal authority;
- hard break, soft break, indentation, and optional-space opportunities;
- generated-position tracking and optional source/MIR/name marks;
- deterministic printer input independent of dictionary iteration or host culture.

This is a structured printer IR, not a JavaScript semantic IR. It does not authorize broad optimization or a reusable compiler framework.

## Helper and runtime factoring

| Strategy | Standalone CLI and Node | Browser/bundler and native modules | Risk and recommendation |
| --- | --- | --- | --- |
| Current self-contained helpers | Excellent: one file executes directly and has no version skew | Repeats helpers across units; script-global composition is awkward | Preserve as Diagnostic baseline and small-program fallback. |
| One deduplicated helper per generated module | Same standalone behavior | Helps every host and tree size; no package skew | First optimization target. Share structurally identical validators only when specialization and invariant behavior remain exact. |
| Generated private runtime prelude | Same file, demand-created | Bundlers can still process it; closure/module isolation improves composition | Recommended Release direction after structured writer. Keep demand gates and stable shape. |
| Imported versioned Copeland JS runtime | Adds a deployment file and compatibility/version contract | Strong cross-module deduplication and cache opportunity; requires ESM/CJS/browser resolution and security policy | Defer. Justify only with multi-module measurements and an explicit runtime version-skew law. |
| Bundler tree-shaken helper imports | Not standalone unless bundled/resolved | Potentially best application-wide deduplication | Optional packaging mode only. Do not require Node, npm, or a minifier at Copeland compile time. |

Naming alone materially reduces raw bytes, but helper/runtime work is the larger architectural opportunity. The required artifact contains 7,728 bytes of repeated Result-validator bodies and 13,126 bytes in two table-create bodies before counting declarations around them. M3 should evaluate imported/runtime factoring only after per-module deduplication and real multi-module benchmarks.

## Safe optimization boundary

Future compact profiles may perform, with cross-profile parity evidence:

- scoped lexical renaming;
- debug-only `Symbol` description removal;
- safe whitespace/token compaction;
- dead generated declaration/helper elimination;
- repeated helper deduplication;
- demand-driven private prelude emission;
- literal pooling only when declaration plus reference bytes win in raw and relevant compressed metrics;
- closed constant folding and branch simplification already proven by MIR semantics;
- single-use temporary folding only when evaluation order, exactly-once behavior, and debugging policy remain intact;
- static descriptor helper reuse only after shape/performance/invariant measurement.

Reject code golf that changes evaluation order, exactly-once evaluation, short-circuiting, binary64 behavior, Unicode code-unit/scalar behavior, Result propagation, typed handler transfer, nominal provenance, object shapes, table/record immutability, or terminal invariant behavior. Release must not introduce `eval`, `with`, coercion tricks, prototype-dependent semantics, host-exception Result flow, polymorphic shapes for shorter spelling, or known deoptimization hazards.

## Source maps and diagnostics

Frontend diagnostics retain `(Position, Length, SourcePath)`, and syntax/bound nodes retain source structure during compilation. Cope MIR nodes and `MirProgram` contain no source spans or stable node IDs. `JavaScriptCompilation` returns only `SourceText` and diagnostics. The backend records no generated positions. Therefore there is no current JavaScript-to-MIR or JavaScript-to-source mapping infrastructure.

Mapping has four distinct products:

1. Compact binding to Diagnostic semantic name: the structured writer can emit this immediately from `BindingId` metadata.
2. Generated JavaScript to Cope MIR: emission events can carry a deterministic MIR path such as function/statement/expression ordinal without changing source semantics.
3. Generated JavaScript to Copeland TS source: requires retained source spans in a lowering side table or debug metadata associated with stable MIR paths; current MIR alone cannot supply it.
4. Runtime diagnostics/stack traces: require external source-map publication and retained invariant messages; `Symbol` descriptions are only a debugger aid.

M0b should make generated-position and optional mark sinks possible but need not ship a source map. M1 may ship compact-name metadata. M2 may introduce external source-map v3 output after deciding how compilation source paths and source text are represented. Do not place verbose provenance back into Release bindings merely to compensate for a missing map.

## Temporary experiments

All files were created under ignored `bin/cts-js-emit-m0a` and did not enter the tracked diff. A token-aware experimental scanner replaced only identifier tokens outside strings/comments. It is not a production scope-aware renamer. No installed Terser, esbuild, SWC, or UglifyJS command/package was available; the milestone forbids installation, so no trusted external-minifier claim is made.

Every required-artifact variant passed `node --check` and produced the identical observable line:

```text
["ready",true,[[3],[]],2000]
```

| Variant | Raw | gzip | Brotli | Relative conclusion |
| --- | ---: | ---: | ---: | --- |
| Current Diagnostic | 38,279 | 3,327 | 2,762 | Authority baseline. |
| Leading-indent/blank-line removal only | 34,751 | 3,241 | 2,705 | 9.2% raw reduction; small transfer reduction. |
| Semantic Chinese + ASCII base-54 ordinals | 27,981 | 2,860 | 2,311 | Smallest semantic rename variant. |
| Semantic Chinese + Heavenly Stems | 29,037 | 3,055 | 2,490 | Preferred readability candidate; 24.1% raw reduction. |
| Opaque ASCII generated-name prototype | 24,107 | 2,605 | 2,176 | Rename-only Release comparison, not a safety proof. |
| Debug `Symbol` descriptions removed | 37,997 | 3,234 | 2,704 | Small independent gain. |
| Stems plus readable compaction | 25,509 | 2,965 | 2,416 | Plausible Symbolic shape. |
| Opaque ASCII, compaction, descriptions removed | 20,297 | 2,415 | 2,019 | Plausible lower bound before helper deduplication; not production minification. |

Representative Symbolic output from the real artifact:

```js
function $值造甲(type, tag, payload) {
    const value = Object.freeze(Object.assign(Object.create(null), {
        $type: type, $tag: tag, $payload: Object.freeze(payload)
    }));
    if (type === $枚型乙) $枚印甲.add(value);
    if (type === $枚型甲) $枚印乙.add(value);
    return value;
}

const $录型甲 = Symbol("r1");
const $录印甲 = new WeakSet();
const $录域甲 = Symbol("r1.f0");
```

The experiment proves current Node parsing, identifier legality for the selected sample, compression measurements, and observable parity for one bounded program. It does not prove lexical-scope safety, browser/toolchain breadth, source maps, performance, or production correctness of a rewritten artifact.

## Future acceptance metrics

Each profile milestone reports these independently:

| Metric | Acceptance direction |
| --- | --- |
| Raw UTF-8 source bytes | Report every representative artifact and geometric/total corpus change. No universal threshold substitutes for parity. |
| gzip bytes | Node zlib level 9, fixed tool version and corpus. |
| Brotli bytes | Node Brotli quality 11, fixed tool version and corpus. |
| Parse/compile time | Fresh-process Node parse plus execution compilation, repeated enough for distributions; do not infer from raw bytes. |
| Runtime performance | Warm and cold representative operations with correctness checks; reject shape regressions hidden by size wins. |
| Helper duplication | Count helper bodies/families within one module and across a multi-module bundle. |
| Determinism | Two in-memory emissions plus two CLI emissions must be byte-identical per profile. |
| Owner semantic readability | Owner review of at least table, Result/handler, record/enum, and TSON excerpts; record unresolved vocabulary decisions. |
| Debuggability | Snapshot quality, compact-name manifest, stack trace/source-map behavior, invariant messages. |

The stable benchmark set should include `main-returns-42`, `payload-enum-match`, `result-propagation`, `try-except-success`, `record-basic`, `m2-table-nested`, both TSON encoding corpora, and the CTS-TSON-TABLE-M1 representative. Add a multi-module bundle case before runtime-package work.

A future implementation should add a tracked measurement command rather than copying this ignored probe, for example:

```powershell
dotnet test tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/Copeland.TS.Backend.JavaScript.Tests.csproj --filter FullyQualifiedName~EmissionProfile
node tools/Measure-CopelandJavaScriptEmission.mjs --corpus tests/Copeland --gzip-level 9 --brotli-quality 11
```

The second command is a proposed M1/M2 tool and does not exist in M0a.

## Migration ladder

### CTS-JS-EMIT-M0b

- Introduce backend-local scope/binding identities and the token/event writer.
- Route current Diagnostic emission through it byte-for-byte where practical.
- Add token-separation, string/literal, nested-scope, collision, and deterministic-output tests.
- Add optional generated-position/name-mark sinks, but no public source map.
- Do not add a new default or regenerate artifacts without explicit approval.

### CTS-JS-EMIT-M1

- Ratify the owner-reviewed vocabulary and one ordinal alphabet.
- Implement Symbolic naming/formatting, Unicode table validation, NFC/control/confusable tests, and scope collision tests.
- Add Node parity and a separate Symbolic corpus. Preserve Diagnostic as default and authority.
- Keep `$type`/tag/payload representation unchanged in the first Symbolic slice.

### CTS-JS-EMIT-M2

- Implement Release scoped ASCII allocation, compact token printing, debug-description removal, and dead generated-helper elimination.
- Add compact-name metadata and the source-map foundation selected from actual span work.
- Establish size, parse/compile, runtime, shape, determinism, and bundler-compatibility baselines.
- Remain compatible with external minifiers/bundlers; do not make one a compile-time dependency without separate approval.

### CTS-JS-EMIT-M3

- Deduplicate helper/validator families and add a generated private prelude where measurements justify it.
- Audit multi-module packaging, native modules, bundler tree shaking, and only then any versioned imported runtime.
- Close cross-profile Node/browser semantic parity, performance, source mapping, corpus policy, and documentation.

## Sequencing and unresolved owner decisions

Design occurs now, between CTS-TSON-TABLE-M1 and TABLE-M2. Production JavaScript emission work should wait until CTS-TSON-TABLE-M2/M3 close. Those milestones will add and close table runtime encoding, which is likely to add another large demanded writer family and is better audited before printer migration. One later explicitly approved corpus regeneration should move Diagnostic to the structured writer; interleaving emitter churn with the active table ladder would obscure regressions and hashes.

Owner approval remains required for:

- Heavenly Stems versus ASCII base-54 Symbolic ordinals, and the exact overflow sequence;
- `组` versus `阵` for array;
- `例` for enum case;
- `接` for handler;
- `令` versus `符` for token;
- `印` for brand/provenance;
- `更` versus `写` for update/write;
- `式` versus `模`/`纲` for schema;
- `识` for stable identity;
- `布` versus `真`/`布尔` for Boolean;
- `域` versus `栏` for record field;
- `配` for match;
- whether Symbolic preserves full `Symbol` descriptions by default;
- final filename suffixes and whether compact-name metadata is embedded in source maps or a separate sidecar.

M0a intentionally leaves those taste decisions visible. It does not ratify English verbosity as the only readable form, and it does not pretend arbitrary Unicode shortening is a semantic dialect.
