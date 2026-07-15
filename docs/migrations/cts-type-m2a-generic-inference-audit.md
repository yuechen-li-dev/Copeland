# CTS-TYPE-M2a generic-inference architecture and audit

**Status:** documentation-only milestone. No production compiler behavior changed.

## Initial state

The audit began at `a4486d76eac97dae0fd6ba8898694ae91ac805f8` (`a4486d7 Refactor document workflows and tighten UI handling`) on `main...origin/main`. The upstream divergence was `0 0` and the worktree was clean.

## Audit conclusion

M1b is implemented for field-only erased requirements, named generics, complete explicit closed arguments, bind-once bodies, requirement access, deterministic specialization, closed MIR, C#/JavaScript consumption, and the generic-to-generic/recursion exclusions. M2b now implements ordinary direct-argument inference through `BindCall`, while complete explicit arguments retain the same closed-instantiation path.

CTS-TYPE-M2a selects direct argument/parameter matching as the future M2b mechanism. The full design, exact implementation inventory, structural algebra, contextual-binding rule, bounded iterative algorithm, diagnostics, fixture plan, TSON/backend boundary, and remaining decisions are in [the M2a design](../Copeland/language/copeland-ts-generic-inference-design-cts-type-m2a.md).

## Corrected evidence

- Generic calls are already syntax nodes, and the parser reserves the `<...>(...)` shape after a name without changing comparisons: [`Parser`](../../src/Copeland/Copeland.TS/Syntax/Parser.cs#L790).
- `TypeParameterSymbol`, `RequirementSet`, and stable generic-function identities are frontend symbols: [`Symbols`](../../src/Copeland/Copeland.TS/Semantics/Symbols.cs#L25).
- Existing explicit calls validate closed depth and requirement satisfaction, then call the only specialization cache/factory: [`Binder`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L1955) and [`GetOrCreateClosedInstantiation`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L2101).
- Canonical closed identities include primitive, nominal record/enum/table/row and structural array/Result/column families: [`ClosedTypeIdentity`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L2168). Alias binding resolves to `TypeAliasSymbol.CanonicalType`: [`Symbols`](../../src/Copeland/Copeland.TS/Semantics/Symbols.cs#L67).
- Exact equivalence is `TypeFacts.AreEquivalent`; current assignability is error recovery plus equivalence: [`Types`](../../src/Copeland/Copeland.TS/Semantics/Types.cs#L115) and [`Binder`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L3732).
- Contextual empty arrays, record literals, and Result constructors already demonstrate why inference may not perform circular expected-type search: [`BindArray`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L3195), [`BindObject`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L3209), and [`BindResultConstructor`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L3175).
- Function returns and ordinary argument binding already propagate expected types, but M2b must not consume those contexts as evidence: [`BindReturn`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L1610) and [`BindCall`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L1941).
- Existing M1b diagnostics/bounds and curated generic fixtures are real, including the 8-parameter, 16-depth, 16-per-definition, and 128-per-compilation limits: [`Binder`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L47), [`GenericDiagnosticInventoryTests`](../../tests/Copeland/Copeland.TS.Tests/GenericDiagnosticInventoryTests.cs), and [`Language/generics`](../../tests/Copeland/Copeland.TS.Tests/Language/Valid/generics).
- TSON plans receive concrete identities only: [`TsonEncodeFeatureTests`](../../tests/Copeland/Copeland.TS.Tests/TsonEncodeFeatureTests.cs#L228). Backend and topology boundaries prevent frontend inference symbols entering MIR/backends: [`Validate-CopelandTsTopology.ps1`](../../tools/Validate-CopelandTsTopology.ps1) and [`Validate-DependencyBoundaries.ps1`](../../tools/Validate-DependencyBoundaries.ps1).

## Hash-policy finding

The full specialization identity is authoritative in `_closedInstantiations`; generated names use only the first 16 SHA-256 hex characters. A map detects a conflicting generated name and throws, rather than silently merging, in [`CreateSpecializationName`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L2153). This is not a collision-safe allocation rule or source diagnostic. M2b must add one deterministic outcome and a forced-collision test before its explicit/inferred-reuse proof is closed.

## Documentation changes and validation contract

This milestone adds the M2a design and this audit, and narrowly updates M0a/M1a/M1b/profile/README doctrine so that no historical document implies inference is current behavior. It changes no source, test, fixture, corpus, project, solution, package, or tooling file.

Final validation must check Markdown links/headings/fences/tables/terminology, UTF-8 without BOM, trailing whitespace, a documentation-only diff, `tools/Validate-CopelandTsTopology.ps1`, `tools/Validate-DependencyBoundaries.ps1`, and `git diff --check`. Full builds/tests are intentionally out of scope unless a non-document change appears.
