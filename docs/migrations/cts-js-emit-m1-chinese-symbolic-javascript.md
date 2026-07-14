# CTS-JS-EMIT-M1 Chinese Symbolic JavaScript

## Outcome

CTS-JS-EMIT-M1 adds an explicit executable Symbolic JavaScript profile beside the immutable Diagnostic default. It uses the backend-local M0b binding document and allocator rather than parsing or rewriting Diagnostic JavaScript. A record excerpt is now shaped as:

```js
const $录型甲 = Symbol("$录型甲");
const $录印甲 = new WeakSet();
const $录域甲 = Symbol("$录域甲");
```

The M1 profile keeps the existing direct JavaScript runtime semantics, carrier representations, demand gates, and public/user names. It is not Release: no source maps, output manifests, helper deduplication, runtime package, bundled runtime, or external minifier was added.

## Routing

M0a remains the vocabulary/measurement design authority. M0b remains the structured allocation and Diagnostic-byte-preservation foundation. This record routes the implementation details to [the M1 architecture record](../Copeland/architecture/copeland-ts-symbolic-javascript-emission-cts-js-emit-m1.md). CTS-TSON-TABLE-M3’s Diagnostic corpus remains authoritative and unchanged.

## Verification

The JavaScript backend tests cover the curated Unicode table, Heavenly-Stem boundaries, collision advancement, Symbolic descriptions, default Diagnostic preservation, TSON-helper symbolic naming, exact checked-in `.sym.js` byte stability, and Node `--check`. A pinned Symbolic corpus test suite now records byte lengths and SHA-256 values for primitive, enum, Result, try/except, record, table, TSON record, TSON array, TSON table, and the representative TSON table-asset source. CLI tests cover explicit Diagnostic/Symbolic selection, rejection for non-JavaScript output, rejection of Release/unknown values, and no output on profile-selection failure.

`tools/Measure-CopelandJavaScriptEmission.mjs` now reproduces the checked-in size and timing evidence from the Diagnostic and Symbolic corpus artifacts. On the measured aggregate, Symbolic drops from 180,067 to 135,183 raw bytes and from 17,682 to 16,285 Brotli bytes. The retained 62,425-byte CTS-TSON-TABLE-M2/M3 Diagnostic representative drops to 47,122 bytes Symbolic and from 4,190 to 3,862 Brotli bytes. Exact measured output parity remains equal across every scripted representative.

Remaining M2 work is narrower now: the profile exists, the corpus is checked in, the benchmark route exists, and bounded runtime/parse evidence is recorded. Release, source maps, helper deduplication, runtime packaging, and external minification remain outside M1.
