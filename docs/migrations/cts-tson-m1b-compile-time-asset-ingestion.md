# CTS-TSON-M1b compile-time asset ingestion migration

## Baseline and outcome

Work began from clean revision `cf06105e08d006024a6a0601305601a8f786d4b7` on branch `main`, tracking `origin/main` with no divergence. CTS-TSON-M1b implements the M1a-selected compile-time operation: one explicitly typed nominal record or payload enum can be loaded from a self-described `.obj.ts` or canonical `.tson` asset and realized by both existing backends without a runtime TSON dependency.

The authoritative implementation contract is [Copeland TS compile-time TSON assets](../Copeland/architecture/copeland-ts-compile-time-tson-assets-cts-tson-m1b.md). Historical value, parser, and projection laws remain in [M0a](../Copeland/language/copeland-ts-tson-design-cts-tson-m0a.md), [M0b](../Copeland/architecture/copeland-ts-tson-shared-parser-and-semantic-model-cts-tson-m0b.md), and [M1a](../Copeland/language/copeland-ts-tson-value-projection-design-cts-tson-m1a.md).

## Delivered implementation

- `CopelandCompilationOptions` now carries optional source path, compilation root, and `ICopelandAssetSource`; source-only callers remain unchanged.
- compiler-owned resolution normalizes and bounds literal paths, caches content, and publishes normalized path plus SHA-256 dependency evidence;
- the binder recognizes exact top-level `$schema` metadata and assigns stable identities to existing record/enum symbols;
- `tsonAsset` is accepted only in an explicitly annotated local `const`, with one literal relative `.obj.ts`/`.tson` path and a record/enum expected type;
- the M0b reader owns parsing, restriction, catalog, resource-limit, and canonical validation;
- exact reachable compiled schema and value identities are checked before recursive expansion into existing bound primitives, record constructions, and enum values;
- MIR and both backend public surfaces remain TSON-free;
- the CLI supplies filesystem access only at composition and preserves existing no-fresh-artifact/stale-output behavior on failure;
- diagnostics distinguish intrinsic, resolution, identity/schema, and `$schema` failures while retaining M0b and parser identifiers for asset-owned failures.

## Production defect

Special TSON numbers demonstrated a general C# numeric literal defect. `CSharpLiteralWriter` could emit invalid NaN/infinity source and its custom finite format did not own all binary64 values. The general writer now preserves NaN bits, signed zero, infinities, and invariant round-trip finite values. `TsonAssetRuntimeTests.General_CSharp_number_lowering_supports_all_binary64_categories` is the non-TSON regression.

## Evidence inventory

- frontend: `TsonAssetFeatureTests` plus `TsonAssets/Valid/{record,enum}` and `TsonAssets/Invalid/missing`;
- M0b regression: `TsonFeatureTests` and `TsonFixtureTests`;
- backend/runtime: `TsonAssetRuntimeTests`, including three C# and two Node repetitions of the representative trace;
- CLI: three emit targets from a filesystem asset and failed-asset stale-output preservation;
- topology: parser uniqueness, excluded variants, TSON-free MIR/backends, fixture ownership, and project-cycle checks.

Node used for runtime evidence is `v26.2.0`. Final test counts, timings, and checked-in artifact hashes are recorded in the completion report produced with this migration rather than embedded as claims before the full validation lane completes.

The stabilized corpus pins these SHA-256 hashes: `main.ts` `662e4abf48cb939fae86ab9a28ce2377f84e917c3440be2bc9d45140f1d3e63f`; `settings.obj.ts` `d2565ff75f6199ee14444ee607eacabd6d2f0e35c0dc2b3df60436ba05655310`; `main.cope` `9e6bd14910ebbc0862138cd15a000f836b047fedf5e86ab24102e117d09c8d95`; `main.g.cs` `bbb11645f4f3c00ef6e5c82023d0831b268fe4df791ea063924d32a472f8185d`; and `main.g.js` `fdcbc7acc01ecf3b290227be15babdba28bb6c480f44158eedc4427d5c5a4290`.

## Explicit absence

No runtime parser, runtime decoding/encoding, TSON runtime package, second lexer/parser, TSON MIR/backend node, JSON implementation, TSON array/Result/table/optional variant, structural runtime object, reflection, dynamic traversal, imports, package resolution, network access, package/version change, commit, push, publish, or NativeAOT claim was introduced.
