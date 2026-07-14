# CTS-JS-EMIT-M0b scoped writer and Diagnostic preservation

## Outcome

CTS-JS-EMIT-M0b adds the first production-backed scoped binding and token-writer foundation to the JavaScript backend while retaining the current Diagnostic output contract. It does not alter CLI surface, generated JavaScript fixtures, package dependencies, MIR, TSON/table behavior, helper population, or runtime semantics.

The implementation adds backend-internal scope/binding identities, function-scope ownership for generated temporaries, a Diagnostic allocator that preserves `__cope_m3_` spelling and collision behavior, document validation, an event-backed Diagnostic line printer, a token/trivia writer, focused collision/token tests, and topology checks. Existing compiler-owned JavaScript syntax templates are retained as bounded `TextPart` events; generated bindings are recorded as typed `BindingPart` references rather than repeated string-name lookups.

## Preservation evidence

The backend corpus comparison passed with 89 tests, including exact `.g.js` comparison and pinned hashes. The newly added hostile-name regression proves that a user function named `__cope_m3_panic_0` remains callable while the generated runtime helper deterministically moves to `__cope_m3_panic_1`.

The table/TSON authoritative artifacts remain unchanged:

| Artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| `TsonTableAssets/Corpus/representative/main.g.js` | 38,279 | `F8AB4406E60F859CE9944904CC1E41070CB291B9AE72B5A5D9C90D58B3126E5A` |
| `TsonEncoding/Corpus/tables-m2/main.g.js` | 62,425 | `D7363BCD7050B8A255E290CDCEF7CC633A6250EF887731DD78539BFC4BA19EF9` |

No `.g.js` artifact was regenerated.

## Benchmark reproduction

Node zlib gzip level 9 and Brotli quality 11 over the checked-in Diagnostic bytes reproduced the M0a set without a claimed size change:

| Case | Raw | gzip | Brotli |
| --- | ---: | ---: | ---: |
| Primitive | 156 | 124 | 110 |
| Payload enum | 4,536 | 844 | 754 |
| Result propagation | 1,853 | 603 | 523 |
| Typed `try`/`except` | 4,651 | 1,020 | 908 |
| Immutable record | 3,967 | 748 | 654 |
| Record table | 23,201 | 2,470 | 2,150 |
| TSON record asset | 1,862 | 567 | 488 |
| TSON record encoding | 14,918 | 2,590 | 2,321 |
| TSON array encoding | 25,224 | 3,292 | 2,878 |
| CTS-TSON-TABLE-M1 | 38,279 | 3,327 | 2,762 |
| CTS-TSON-TABLE-M2/M3 | 62,425 | 5,073 | 4,190 |

## Validation added

- stable ordinal allocation, compiler-local origin, distinct binding identities, hostile user-name collision avoidance;
- nested function/block visibility, shadowing through distinct binding identities, unresolved/out-of-scope/undeclared document rejection, and reserved-name rejection;
- token word/number separation, `+` adjacency, numeric member access, escaping (quotes, slash, controls, Unicode, lone surrogate), completion/final-LF, and indentation/punctuator rejection;
- direct backend hostile-name regression and typed Diagnostic binding-event/scope regression;
- topology checks for the one backend-local allocation/token model, absent external minifier/source-map output paths, and absent premature Symbolic/Release allocator implementation.

## Boundary

The remaining 149 fixed compiler-syntax templates may be replaced incrementally with direct token events in a future migration. They are not an arbitrary raw-text escape hatch: there is no public API for one, and generated binding references are structured events. This milestone does not claim Symbolic output, Release output, minification, source maps, helper deduplication, or runtime packaging.
