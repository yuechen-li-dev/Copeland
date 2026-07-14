# CTS-TSON-M2b runtime canonical encoding migration

**Status:** implemented and validated on 2026-07-14.

## Baseline

Implementation began at revision `95ec9f8b12a14e6b5f7292cfc93df64440f15cda` on branch `main`, tracking `origin/main`. The worktree already contained the accepted M2a documentation changes in the Copeland index, M1b architecture, language profile, M2a design, and M2a audit; those changes were preserved and routed forward rather than replaced.

## Scope delivered

CTS-TSON-M2b adds the reserved `tsonEncode(value)` intrinsic, compiler-owned `TsonEncodeError`, demand-created `MirTsonEncodingPlan`, `MirTsonEncodeExpression`, shared MIR validation, deterministic `.cope` rendering, and closed C#/JavaScript canonical writers. The authoritative architecture and exact laws are recorded in [the M2b architecture](../Copeland/architecture/copeland-ts-runtime-tson-encoding-cts-tson-m2b.md).

The implementation retains stable identity only for demanded reachable declarations, orders nominal definitions by ordinal name, preserves member declaration order, and excludes unused declarations. Repeated roots deduplicate plans and helpers. No-intrinsic programs retain their previous runtime topology.

## Fixtures and proof inventory

- `TsonEncodeFeatureTests` covers binding, reachability, ordering, composition, diagnostics, reservation, and demand creation.
- `MalformedTsonEncodingPlanValidationTests` sends the shared malformed-plan matrix through both backends and proves artifact suppression.
- `TsonEncodeRuntimeTests` covers exact C#/Node output, M0b reparsing, binary64, strict Unicode, exact limits, demand emission, adversarial carriers, exactly-once emission, empty record/enum roots, asset round trip, and corpus hashes.
- `CliIntegrationTests` covers MIR/C#/JavaScript emission, repeat determinism, and stale-output preservation on failure.
- `TsonEncoding/Corpus/record` contains source, asset, `.cope`, generated C#, generated JavaScript, and expected canonical output.

## Runtime law

The fixed maximum is 1,048,576 canonical UTF-8 bytes including static schema text, value envelope, punctuation, indentation, and final LF. Each runtime string is limited to 262,144 UTF-16 code units. Precedence is per-string length, invalid Unicode, then bounded total output. Output is committed only on success.

All NaNs normalize to `7FF8000000000000`; every other binary64 value preserves logical bits in uppercase 16-digit form. Both backends explicitly validate surrogate pairs and count UTF-8 bytes without replacement fallback.

## Production defects

No unrelated production defect required a compatibility workaround. The implementation placed canonical static text construction and malformed-plan validation in the shared MIR ownership layer so backend parity does not depend on duplicated frontend or printer logic.

## Validation record

Validation used .NET SDK 10.0.301 and Node `v26.2.0`. The focused source suite passed 15 cases; the focused runtime/shared malformed-MIR suite passed 19 cases, including 11 malformed plan variants through both backends; and the focused CLI suite passed 4 cases. Full validation passed:

- `Copeland.TS.slnx`: build in 1.16 seconds; 608 tests passed.
- `Copeland.slnx`: final rebuild in 1.14 seconds; 718 tests passed.
- `JointTaskForce.slnx`: final rebuild in 2.78 seconds; 1,971 tests passed.
- `Validate-CopelandTsTopology.ps1` and `Validate-DependencyBoundaries.ps1`: passed.
- `git diff --check`: passed.

The full required build/test sequence completed in 36.62 seconds on the validation machine. Repeated CLI emission and runtime parity were byte identical. The pinned corpus SHA-256 values are:

| Artifact | SHA-256 |
| --- | --- |
| `main.ts` | `72F3E1EDF2CEA75029722BA16C8DDEA9F8F9DFB7891D1477A973F4C3BF66214F` |
| `settings.obj.ts` | `FCF039A91E0157C47AC3B2B3578101001FBE0F65ABBC4F85ED0C0D009AED0C5F` |
| `expected.tson` | `F7754A6EBDFF2D2429EAF8AF06479F855043EF70911DBF2AF964CDA1815D5647` |
| `main.cope` | `CD02076AB1D5D53860643FBBB11235AAE1087E506E9372D78A733F291F787119` |
| `main.g.cs` | `F0C0A6BF3B9546C2D575E762B7E3AA8C90F1153A96C25DCB81ACF3288F78AB37` |
| `main.g.js` | `6FE85C34DE3FDBAD1C4917AE08AE94D8F752F0828EEB0642BD535EB7D25E69D9` |

Existing corpus files were not regenerated or changed; the six hashes above belong to the new M2b corpus. Machina's slow lane was not run because no Machina-owned infrastructure changed. No NativeAOT publish lane was run, so no NativeAOT validation claim is made.

## Exclusions confirmed

No runtime parser, runtime filesystem access, compiler-host TSON runtime dependency, JSON API, reflection, `dynamic`, public runtime `TsonValue`, structural object traversal, property enumeration, TSON array/Result/table/optional variant, cross-schema support, package/version change, commit, push, or publish was introduced.
