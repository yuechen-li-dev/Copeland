# CTS-JS-EMIT-M0a JavaScript output and Symbolic dialect audit

## Outcome

CTS-JS-EMIT-M0a is a documentation-only architecture audit for future Diagnostic, Symbolic, and Release JavaScript emission. It selects a backend-local scope/binding/token-event writer, keeps current Diagnostic output and `--emit javascript` behavior authoritative, proposes a Chinese semantic codebook and Heavenly-Stem ordinal candidate, records bounded Node/compression experiments, and defers all production emission work until CTS-TSON-TABLE-M2/M3 close.

No production source, test, fixture, generated corpus artifact, hash, CLI behavior, MIR, runtime behavior, package manifest, lockfile, dependency, project, solution, build target, package version, or source map changed.

## Baseline and preservation

Work began at revision `6561121588b94fc9cf6efdce1387aae6e5538318` on branch `main`, tracking `origin/main`. `git status --short --branch` reported `## main...origin/main`; there were no pre-existing tracked or untracked user changes. Temporary experiment files were confined to ignored `bin/cts-js-emit-m0a`.

The audit did not commit, push, publish, install a minifier, or modify anything outside the repository.

## Repository evidence

The current backend is a BCL-only `Copeland.TS.Backend.JavaScript -> Copeland.TS.Mir` project. `JavaScriptBackend.Emit` validates MIR, collects nominal/Result/table metadata, allocates collision-avoiding global `__cope_m3_...` bindings after user bindings, and emits strings through a four-space line writer. Its statementful `EmittedExpression` preludes preserve exactly-once order and typed Result/handler flow.

Current demand behavior is real but module-local:

- primitive-only programs emit no runtime prelude;
- enum/Result/record/table programs demand their private token, validator, and construction families;
- unwrap and `try`/`except` demand panic and structured-flow helpers;
- TSON plans demand one closed writer closure and array helpers only for array plans;
- every standalone artifact repeats the families it demands.

Private representation evidence includes `Symbol`-keyed record/table slots, frozen null-prototype carriers, `WeakSet` created-instance provenance, private textual enum/Result/flow fields, exact descriptor validation, terminal invariant/unwrap throws, and Result-valued ordinary bounds/handler flow without host exceptions. Node harnesses append calls to generated top-level functions, use unique temporary directories, close stdin, drain both output streams, enforce a timeout, and terminate process trees on timeout.

Corpus ownership is split deliberately. `JavaScriptCorpusTests` owns exact backend `.g.js` comparison and several stable hashes. TSON runtime/asset tests own their multi-artifact fixed points and hashes. `TsonTableAssetFeatureTests` pins the required `main.g.js` at 38,279 bytes and SHA-256 `F8AB4406E60F859CE9944904CC1E41070CB291B9AE72B5A5D9C90D58B3126E5A`. CLI integration writes stdout or one explicit `--out`, preserves stale outputs on compilation failure, and accepts only `mir`, `csharp`, or `javascript`; it has no profile option or implicit JavaScript filename policy.

Frontend diagnostics retain position, length, and optional source path. Cope MIR has no source span or stable node ID, `JavaScriptTextWriter` records no generated locations, and `JavaScriptCompilation` returns only source text and diagnostics. No JavaScript source-map implementation exists.

## Measurements

Measurements used current checked-in bytes, Node v26.2.0, zlib 1.3.1 gzip level 9, and Brotli 1.2.0 quality 11.

| Case | Source | JS | gzip | Brotli | Obvious scaffold |
| --- | ---: | ---: | ---: | ---: | ---: |
| Primitive | 195 | 156 | 124 | 110 | 9.6% |
| Payload enum | 457 | 4,536 | 844 | 754 | 80.3% |
| Result propagation | 190 | 1,853 | 603 | 523 | 72.0% |
| Typed `try`/`except` | 198 | 4,651 | 1,020 | 908 | 59.5% |
| Immutable record | 212 | 3,967 | 748 | 654 | 71.1% |
| Record table | 504 | 23,201 | 2,470 | 2,150 | 83.9% |
| TSON record asset | 238 | 1,862 | 567 | 488 | 87.7% |
| TSON record encoding | 370 | 14,918 | 2,590 | 2,321 | 96.8% |
| TSON array encoding | 542 | 25,224 | 3,292 | 2,878 | 94.4% |
| CTS-TSON-TABLE-M1 | 971 | 38,279 | 3,327 | 2,762 | 86.8% |

For the required artifact, generated lexical names occupy 14,915 occurrence bytes across 528 occurrences and 106 distinct bindings. Eight near-isomorphic Result validators total 7,728 bytes. All top-level validators total 14,812 bytes; construction/create functions total 15,218 bytes. The repeated immutable descriptor phrase occurs 41 times for 2,255 raw bytes. Twenty-seven `Symbol` descriptions cost 282 raw bytes. Indentation and blank-line removal saves 3,528 raw bytes. These values overlap and are not additive.

The audit therefore rejects “rename only” as the complete size architecture. Renaming is a large raw-source opportunity, while helper specialization, closed table construction, validation, and standalone duplication are larger structural concerns. Compression reduces the apparent cost of repetition, so raw, transfer, parse, and runtime metrics remain independent.

## Experiments

An ignored token-aware prototype rewrote compiler-generated identifiers outside string/comment tokens. It was deliberately not applied to checked-in files and is not claimed as a production-safe scoped renamer. No Terser, esbuild, SWC, or UglifyJS installation was present, and installing one was prohibited, so the audit makes no trusted-minifier result claim.

Every required-artifact variant passed `node --check` and executed with the same observable output `['ready', negative-zero-is-negative-zero, [[3], []], 2000]`, serialized as `["ready",true,[[3],[]],2000]`.

| Variant | Raw | gzip | Brotli |
| --- | ---: | ---: | ---: |
| Current Diagnostic | 38,279 | 3,327 | 2,762 |
| Whitespace-only | 34,751 | 3,241 | 2,705 |
| Chinese semantics plus ASCII base-54 | 27,981 | 2,860 | 2,311 |
| Chinese semantics plus Heavenly Stems | 29,037 | 3,055 | 2,490 |
| Opaque ASCII generated-name prototype | 24,107 | 2,605 | 2,176 |
| Debug `Symbol` descriptions removed | 37,997 | 3,234 | 2,704 |
| Stems plus readable compaction | 25,509 | 2,965 | 2,416 |
| Opaque ASCII, compact, descriptions removed | 20,297 | 2,415 | 2,019 |

Node identifier probes accepted the three intended forms `$表列存a`, `$表列存甲`, and `$表列存一`. They also accepted combining-mark, zero-width-joiner, and variation-selector cases that the proposed policy forbids. Raw emoji failed as a lexical binding but worked as a quoted property and `Symbol` description. Canonically equivalent identifiers remained distinct at runtime, confirming the need for NFC validation and alias rejection.

The full codebook, 40 real-name translations, ordinal measurements, Unicode law, collision policy, helper alternatives, and source-map staging are in the [M0a design authority](../Copeland/language/copeland-ts-javascript-emission-profiles-design-cts-js-emit-m0a.md).

## Architecture decision

The selected design is option B: a backend-local structured writer with lexical scopes, binding identities, typed token/literal events, formatting opportunities, and optional source/name marks. Direct string emission plus another allocator does not safely own compact token separation or nested-scope reuse. A complete JavaScript AST is not justified by the emitted subset. An external minifier cannot own the Symbolic dialect and would add an unapproved compile-time tool boundary.

The profile contracts are:

- **Diagnostic:** current provenance-rich names, stable layout, descriptions, exact corpus authority, default behavior.
- **Symbolic:** stable `$`-prefixed Chinese semantic compounds, deterministic reviewed ordinals, moderate readable formatting, optional mapping, direct Node/browser execution.
- **Release:** shortest safe scoped ASCII, compact tokens, no debug descriptions, dead helper elimination, external provenance maps, deterministic but not hand-readable.

Stable TSON identities, canonical data, public/exported/interop names, user-visible diagnostics, and externally accessed properties remain unmangled in every profile. Top-level user functions remain conservatively fixed until a module/export/entrypoint law exists.

## Migration and sequencing

The bounded ladder is:

1. CTS-JS-EMIT-M0b: structured writer and scope/binding allocator, preserving Diagnostic bytes where practical.
2. CTS-JS-EMIT-M1: owner-ratified Symbolic vocabulary/ordinals, Unicode validation, Node parity, and separate Symbolic corpus.
3. CTS-JS-EMIT-M2: Release printer, description/dead-helper removal, mapping foundation, and independent size/performance baselines.
4. CTS-JS-EMIT-M3: measured helper/runtime deduplication, multi-module packaging audit, cross-profile parity, and closeout.

Only design belongs between CTS-TSON-TABLE-M1 and TABLE-M2. [CTS-TSON-TABLE-M3](../Copeland/architecture/copeland-ts-tson-table-closeout-cts-tson-table-m3.md) is now closed; it retained the 62,425-byte Diagnostic artifact unchanged. CTS-JS-EMIT-M0b may therefore begin only under separately approved scope. JavaScript corpus regeneration remains a later explicitly approved migration.

## Files changed

- `docs/Copeland/language/copeland-ts-javascript-emission-profiles-design-cts-js-emit-m0a.md`
- `docs/migrations/cts-js-emit-m0a-javascript-output-and-symbolic-dialect-audit.md`
- `docs/Copeland/README.md`
- `docs/Copeland/language/copeland-ts-language-profile.md`
- `docs/Copeland/architecture/copeland-ts-javascript-backend-cts-m1.md`
- `docs/Copeland/architecture/copeland-ts-tson-table-assets-cts-tson-table-m1.md`

## Validation

Validation is documentation-scoped. The final six-file Markdown allow list and changed-extension scan passed, with no generated JavaScript, production C#, project, solution, manifest, lockfile, props, or targets diff. Local Markdown links, linked anchors, cited repository paths, unique heading anchors, consistent table columns, balanced code fences, UTF-8 decoding, no BOM, NFC, and control-character checks passed. The longest new design table row is 312 code points; all ten tables have consistent row shapes.

The codebook scan found 40 unique entries. Every entry and both ordinal alphabets are NFC and use the required identifier properties; no combining mark, CJK compatibility ideograph, bidi control, zero-width/default-ignorable character, variation selector, duplicate entry, or forbidden control occurs. The temporary variants reproduced all raw/gzip/Brotli measurements, passed `node --check`, and produced identical Node output. The required checked-in artifact remained 38,279 bytes with SHA-256 `F8AB4406E60F859CE9944904CC1E41070CB291B9AE72B5A5D9C90D58B3126E5A`.

`pwsh -NoProfile -File tools/Validate-CopelandTsTopology.ps1` passed. `pwsh -NoProfile -File tools/Validate-DependencyBoundaries.ps1` passed for 27 production projects with no permitted exceptions. `git diff --check` passed for tracked changes, and the equivalent no-index whitespace check passed for both new documents.

Full builds/tests are intentionally not part of this documentation-only milestone. No behavioral claim extends beyond the bounded temporary Node experiments and inspection of existing repository evidence.
