# CTS-TYPE-M1a interface requirement audit

**Outcome:** documentation-only architecture success. The authoritative record is [CTS-TYPE-M1a field-only interface requirements](../Copeland/language/copeland-ts-interface-requirements-design-cts-type-m1a.md).

## Baseline

Audit began at `ff71e3a32ad05128a98db2f6fc8e3c9e340fab1d` (`ff71e3a Implement transparent compilation-unit type aliases`) on `main`, tracking `origin/main`, with upstream divergence `0/0` and a clean worktree. No unrelated change was present.

## Corrected inventory and conclusions

M0b is closed: `type` is a contextual compilation-unit declaration, aliases share the compilation-unit type-name collision pass, resolve to canonical types, and disappear before MIR. M1a corrects the stale M0a/profile implication that aliases have no fixtures; M0b added alias fixtures. Interfaces, `extends`, `implements`, `readonly`, optional members, method syntax, generic angle brackets as an accepted form, and a singleton `&` token remain unimplemented.

The audit confirms concrete record and row field access from syntax through [`Binder.BindMemberAccess`](../../src/Copeland/Copeland.TS/Semantics/Binder.cs#L2848), dedicated bound nodes, [`MirLowerer`](../../src/Copeland/Copeland.TS/Lowering/MirLowerer.cs#L237), validated concrete MIR nodes, and C#/JavaScript emission. It therefore selects a frontend-only requirement-member access that is specialized to those existing access nodes before canonical MIR.

Interfaces are contextual compilation-unit, field-only erased requirement sets, legal only in future generic constraints. They structurally accept nominal immutable records and nominal table-row views (including transparent aliases to them) when every required readable field has exactly equivalent canonical type. They do not create storage, identity, equality, adapters, TSON schemas, backend carriers, or CLR interfaces. Constraint-only `&` is a deterministic conjunction; repeated named interfaces are diagnosed as redundant and conflicting field types are diagnosed at the constraint.

The staged recommendation is not an inert declaration milestone: combine field-only interfaces with one explicit, closed generic-function vertical slice. Keep generic definitions and requirement symbols frontend-only and specialize each closed body to existing record/row accesses before MIR.

## Documentation changes and validation

This milestone adds the authoritative design and this audit, then narrowly updates the M0a record, M0b record, canonical language profile, and Copeland documentation index. It changes no production, test, fixture, corpus, project, package, solution, or tooling file; no compiler/runtime behavior changes.

Validation must run from the final documentation-only worktree:

- `tools/Validate-CopelandTsTopology.ps1`
- `tools/Validate-DependencyBoundaries.ps1`
- Markdown-link/heading/table/fence/terminology review, UTF-8 without BOM, trailing-whitespace check, and `git diff --check`

Full builds/tests are intentionally out of scope unless a non-document change appears.
