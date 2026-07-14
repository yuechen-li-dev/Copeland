# CTS-TYPE-M0b transparent aliases migration

## Baseline and corrected inventory

Implementation began from clean `main` revision `bb4ddbbbffb68084316b6fb01418b02087b3df2d`, tracking `origin/main` with divergence `0/0`. The M0a inventory was accurate: `type` lexed as an identifier; the parser had no alias declaration; `TypeSymbol` had only canonical primitive, structural, proof-era error-name, and nominal families; the binder used nominal family dictionaries plus one value-shaped lexical scope; MIR and both backends had no alias form. The concrete correction exposed by implementation was that direct `void` could bind in ordinary value positions despite M0a's stated return/Result-success boundary. `COPE-TYPE-0020` now enforces that canonical law for direct and aliased types alike.

## Change summary

- Added contextual compilation-unit `type Name = ExistingType;` syntax and alias-owned malformed/generic recovery.
- Added compile-time-only `TypeAliasSymbol`, declaration-order collision analysis, nominal/alias predeclaration, forward resolution, authored mismatch provenance, and distinct value lookup.
- Added explicit non-recursive dependency collection, cycle discovery, and canonical resolution with bounded deterministic paths.
- Preserved canonical `TypeSymbol` values through bound executable nodes and erased aliases before MIR.
- Added focused parser/binder/MIR/TSON/backend/CLI tests and filesystem language fixtures.
- Added the [architecture record](../Copeland/architecture/copeland-ts-transparent-type-aliases-cts-type-m0b.md) and narrowed the M0a/profile indexes.

## Evidence

The language fixture inventory moves from 28 valid/67 invalid to 31 valid/83 invalid: three valid alias fixtures and sixteen invalid alias fixtures. Focused tests cover contextual parsing, malformed recovery, generic rejection, primitive/array/Result/record/enum/table positions, forward nominal and alias references, type/value same-name behavior, all nominal collision families, unknown recovery, direct and indirect cycles, deterministic paths, a 5,000-alias chain, expected types, equality, contextual records, `void`, diagnostic provenance, TSON identity, MIR structural erasure, exact backend equality, and CLI artifact policy.

No alias corpus was added, so there is no new checked-in corpus hash. Existing checked-in MIR, C#, JavaScript Diagnostic, and JavaScript Symbolic corpora remain the byte-stability oracle and pass unchanged. The focused alias/direct equivalence tests require exact source equality rather than accepting a structural approximation.

## Validation

The closeout validation includes focused alias tests, all filesystem fixtures, the complete Copeland TS solution, CLI integration, affected record/Result/table/TSON/backend/runtime suites through the full solution lanes, all requested builds/tests, both topology validators under PowerShell 7, fixture/content checks, documentation checks, no-carrier searches, and `git diff --check`. See the implementation report for the command-level results from the final worktree.

## Closure

CTS-TYPE-M0b is closed for transparent non-generic compilation-unit aliases. Interfaces, generics, constraints, general TypeScript type-level programming, modules, static evaluation, and runtime alias identity remain excluded.
