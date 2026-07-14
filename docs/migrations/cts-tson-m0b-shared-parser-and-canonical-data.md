# CTS-TSON-M0b shared parser and canonical data migration

## Baseline and convergence

Work began on branch `main` at `0733e2a50af16e369a50d02f5f0d6c420abb40d6` with a clean worktree. The milestone converged through a temporarily colocated TSON integration, not syntax extraction. The production entry point is `Copeland.TS.Syntax.SyntaxTree.Parse(string)`.

## Delivered surface

- `Copeland.TS.Tson` contains six closed immutable value variants, schema/catalog definitions, document profiles, limits, diagnostics, shared-parser reading/projection, and canonical printing.
- `.obj.ts` supports comments/noncanonical layout and explicit authoring identity when `$schema` is omitted.
- `.tson` requires embedded identity and exact canonical bytes.
- The canonical envelope uses `$schema`, restricted record/enum declarations, and exactly one `$value` binding.
- Stable identities are `schema#Type`, `schema#Type.field`, `schema#Enum.Case`, and `schema#Enum.Case.payload`.
- Canonical binary64 uses `$number("16 uppercase hexadecimal bits")`; NaNs normalize, signed zero and infinities survive.
- Nominal schema cycles are rejected in M0b; structural value trees are eager, bounded, and alias-free by construction.
- Diagnostics use `COPE-TSON-0001` through `0005`; ordinary syntax failures retain production parser codes and spans.

## Proof ownership

`TsonFeatureTests` owns semantic construction, parser reuse, profiles, nominal isolation, record/enum validation, all resource limits, numeric categories, Unicode, semantic round-trip, and byte idempotence. `TsonFixtureTests` owns filesystem fixture discovery and profile execution. `Validate-CopelandTsTopology.ps1` owns parser/lexer duplication, dependency, excluded-variant, fixture, and project-cycle checks.

No existing Copeland parser, binder, MIR, backend, CLI, table, record, or enum behavior was changed. No production defect required an owning fix. Existing `.cope`, `.g.cs`, and `.g.js` artifacts remain input expectations and are not rewritten by this milestone.

## Explicitly absent

There is no JSON code, table/Result/array TSON variant, runtime carrier bridge, MIR lowering, C#/JavaScript backend dependency, CLI or MSBuild handling, file association, import/module execution, reflection, host serialization, package change, commit, or publication.

## Follow-up boundary

Do not extract syntax merely for visual topology. Revisit `Copeland.TS.Syntax` as a project only when a second assembly consumer needs the parser without frontend binding/lowering and public type identity can be preserved. Keep table/JSON work blocked on explicit collection and compatibility laws rather than treating backend carriers as data.
