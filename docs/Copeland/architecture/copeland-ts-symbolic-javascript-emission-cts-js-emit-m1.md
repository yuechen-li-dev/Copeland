# Copeland TS Symbolic JavaScript emission (CTS-JS-EMIT-M1)

**Status:** implemented first executable Symbolic profile. Diagnostic remains the default and checked-in artifact authority. Release, source maps, helper deduplication, runtime packaging, and external minification remain deferred.

## Contract

```text
Cope MIR
  -> executable Chinese symbolic JavaScript
  -> browser/Node JavaScript engine
```

Symbolic JavaScript is a lossless-ish semantic compression layer, not a source language, virtual machine, or runtime decoder. `JavaScriptBackend.Emit(program)` remains Diagnostic. The immutable `JavaScriptEmissionOptions` accepts `Diagnostic` or `Symbolic`; an invalid value fails before an artifact is returned. The CLI accepts `--emit javascript --javascript-profile symbolic`. A profile with MIR/C# output, or `release`, is rejected before writing an artifact. An explicit `--out` remains authoritative.

Diagnostic uses `main.g.js` corpus names; Symbolic corpus naming is reserved as `main.sym.js`. One invocation emits one chosen profile only.

## Vocabulary and allocation

The closed M1 vocabulary is versioned `CTS-JS-EMIT-M1`. Core atoms are: `表 行 列 录 枚 项 载 组 果 成 错 流 函 接 型 值 存 符 印 造 验 取 更 编 源 纲 识 界 律 终 助 运 串 数 布 域 序 配`; supporting atoms are `传 解 槽 收 支 临 计 写 返 附`.

Broad carriers come first: `表行型`, `表列存`, `录域`, `录印`, `枚项`, `果验`, `流接`, `串编`, and `纲识`. Bindings are typed at allocation, not inferred by parsing emitted Diagnostic names. Unknown Symbolic roles are invariant failures.

Every compiler-private spelling begins with `$`. Per compound and lexical scope, ordinal spellings use the Heavenly Stems `甲乙丙丁戊己庚辛壬癸`. The M1 continuation follows the ratified visible sequence: `1=甲`, `2=乙`, `9=壬`, `10=癸`, `11=甲甲`, `12=甲乙`, `19=甲壬`, `20=乙癸`, `21=乙甲`; 99/100/101 boundary tests are pinned in the backend test suite. User-visible names are reserved before allocation, so a user `$录型甲` forces a generated `$录型乙`.

## Unicode and formatting law

The vocabulary table permits only its curated Chinese atoms/stems after the `$` prefix. Every final generated name is NFC and checked against that closed table. Output is checked for NFC, surrogates, bidi controls, zero-width/variation selectors, private-use code points, and one final LF. No compiler-generated emoji, escapes, compatibility ideographs, or combining marks are accepted.

Symbolic rendering removes blank separator lines, uses two-space structural indentation, retains one statement per line and semicolons, and preserves literal spelling and execution order. It does not apply a regex/minifier post-pass.

## Names that are not translated

User functions/locals/parameters, public `main`, serialized TSON identities, error strings, tags such as `ok`/`err`, and private object property representation remain exact. Compiler-private Symbol descriptions use the final Symbolic binding spelling; Diagnostic descriptions stay byte-for-byte unchanged.

## Evidence and boundary

The retained Diagnostic table hashes are unchanged: TABLE-M1 `F8AB4406E60F859CE9944904CC1E41070CB291B9AE72B5A5D9C90D58B3126E5A`; TABLE-M2/M3 `D7363BCD7050B8A255E290CDCEF7CC633A6250EF887731DD78539BFC4BA19EF9`.

The backend test suite now pins representative Symbolic corpus bytes and SHA-256 values for primitive, enum, Result, try/except, record, table, TSON record, TSON array, TSON table, and the TSON table-asset representative. Representative hashes include:

| Artifact source | Symbolic bytes | SHA-256 |
| --- | ---: | --- |
| `main-returns-42.ts` | 148 | `5D8B155F9019A9C94DA044E829D27D02CE86CD8FF0CE96A5B34E1E47BB6E9784` |
| `m2-table-basic.ts` | 15,461 | `80AF3FD5ED71D4B9CFCCCDE62877027480255C3E9F841A94C3B77FD9FE46AE5A` |
| `TsonEncoding/Corpus/record/main.ts` | 12,156 | `C227F69DF91785B71786C7CA5AEB406EA031323AD8ACF22E3F5ABAEA1507B79B` |
| `TsonEncoding/Corpus/arrays/main.ts` | 20,419 | `0C0740A327B4A80B81118A9B7884902B1EC78277A90F641B9CA0EF439BF0D591` |
| `TsonEncoding/Corpus/tables-m2/main.ts` | 47,122 | `ACEC71ADB5E76FA85939EEA5789B5EA65543EEBB97D9A9AC55C5ABC8063A89A9` |
| `TsonTableAssets/Corpus/representative/main.ts` | 27,074 | `2B853C15B5628F1DF81E6130EE28F2A2B3E86A443C537568E3AB9FF16DE62C66` |

`node tools/Measure-CopelandJavaScriptEmission.mjs --corpus tests/Copeland --gzip-level 9 --brotli-quality 11` reproduces the pinned size set from the checked-in `.g.js` and `.sym.js` artifacts:

| Case | Diagnostic raw | Symbolic raw | Diagnostic gzip | Symbolic gzip | Diagnostic Brotli | Symbolic Brotli |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `main-returns-42` | 156 | 148 | 124 | 123 | 110 | 109 |
| `payload-enum-match` | 4,536 | 3,752 | 844 | 859 | 754 | 758 |
| `result-propagation` | 1,853 | 1,524 | 603 | 601 | 523 | 518 |
| `try-except-success` | 4,651 | 3,434 | 1,020 | 992 | 908 | 838 |
| `record-basic` | 3,967 | 2,439 | 748 | 697 | 654 | 589 |
| `record-order-with` | 2,697 | 1,654 | 672 | 624 | 600 | 506 |
| `m2-table-basic` | 21,361 | 15,461 | 2,299 | 2,074 | 1,982 | 1,719 |
| `TSON record encode` | 14,918 | 12,156 | 2,590 | 2,498 | 2,321 | 2,182 |
| `TSON array encode` | 25,224 | 20,419 | 3,292 | 3,216 | 2,878 | 2,777 |
| `CTS-TSON-TABLE-M2/M3` | 62,425 | 47,122 | 5,073 | 4,759 | 4,190 | 3,862 |
| `CTS-TSON-TABLE-M1 representative` | 38,279 | 27,074 | 3,327 | 3,017 | 2,762 | 2,427 |
| aggregate | 180,067 | 135,183 | 20,592 | 19,460 | 17,682 | 16,285 |

The scaffold-heavy aggregate therefore drops by 44,884 raw bytes and 1,397 Brotli bytes. The retained M2/M3 representative alone drops by 15,303 raw bytes and 328 Brotli bytes. The small payload-enum case still regresses slightly under gzip/Brotli, which remains acceptable and is reported rather than hidden.

The same tool measures raw generated-binding bytes, `Symbol("...")` description bytes, and raw whitespace bytes across the aggregate:

| Aggregate | Diagnostic | Symbolic |
| --- | ---: | ---: |
| Generated binding bytes | 54,427 | 22,521 |
| Symbol-description bytes | 1,671 | 2,056 |
| Raw whitespace bytes | 34,910 | 24,132 |

Warm in-process Node `vm` measurements stay within the expected noise band for this scope. Across the representative cases, parse medians stay at roughly `0.001` to `0.002` ms and warm execution medians stay within `0.002` to `0.055` ms for both profiles; exact observable output is equal in every measured case. These are bounded comparisons, not a claim of universal performance improvement.

Focused backend, CLI, and Node parse tests prove profile selection, allocation boundaries, Symbolic descriptions, collision advancement, TSON-helper symbolic naming, direct Node parsing, checked-in `.sym.js` byte stability, and repeated Symbolic parity. Release, source maps, helper deduplication, runtime packaging, and external minification remain deferred.
