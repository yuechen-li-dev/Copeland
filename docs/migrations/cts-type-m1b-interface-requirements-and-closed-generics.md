# CTS-TYPE-M1b migration record

The working tree began from `2e4c64fd4b7d2a1d4282d2a14470a11c3dddad64` on `main...origin/main` and preserved the existing uncommitted M1b vertical slice rather than restarting it.

This migration pass tightens the original slice in four ways:

1. specialization identity no longer depends on record/table traversal ordinals such as `r1`
2. generic/interface resource bounds are explicit frontend rules
3. generic recursion and generic-to-generic calls are deliberate M1b exclusions with diagnostics
4. proof coverage now includes frontend inventories, filesystem fixtures, runtime parity, and CLI artifact determinism

## Implemented shape

- `interface` is a contextual compilation-unit declaration with field-only members
- generic bodies bind once against frontend requirement facts
- explicit closed calls specialize by substitution over the already-bound body
- requirement member access rewrites to ordinary record or table-row access before MIR
- MIR/backends remain concrete-only

## Stable specialization identity

Specialization cache keys are now based on:

```text
function stable identity + ordered canonical closed type identities
```

Nominal closed types use declaration-name-based stable identities rather than ordinary MIR ordinals. Specialized function display names append the first sixteen hexadecimal digits of `SHA-256(UTF-8(identityText))`.

## Added evidence

- focused frontend tests for stable identities, nested identities, bind-once behavior, table-row rewrite, requirement diagnostics, and resource limits
- focused runtime parity test covering C# and Node on unconstrained and constrained generic paths
- expanded valid/invalid filesystem fixtures under `Language/*/generics`
- CLI emission contract test with pinned MIR/C#/JS lengths and SHA-256 values

Node evidence was recorded on `v26.2.0`.

## Bounds enforced in the current implementation

- 8 type parameters per function
- 8 required interfaces per type parameter
- 32 normalized requirement fields per type parameter
- 128 interface fields per compilation
- 16 closed-type nesting depth
- 16 closed instantiations per generic definition
- 128 closed instantiations per compilation
- 4 requirement-field entries per diagnostic before truncation

## Exclusions retained

M1b still excludes inference, generic nominal declarations, interface runtime/storage values, generic-to-generic calls, generic recursion, general intersections, and new TSON behavior.

## Closure ledger

| Requirement family | Status | Evidence |
| --- | --- | --- |
| Contextual field-only interface syntax | Satisfied | parser tests, fixture theories, binder diagnostics |
| Explicit generic type parameters and calls | Satisfied | parser tests, binder tests, runtime parity, CLI |
| `&` only inside constraints, not expressions | Satisfied | parser ambiguity tests |
| Unconstrained generics as empty requirements | Satisfied | valid fixtures, runtime parity, MIR specialization tests |
| Constrained records and aliases | Satisfied | valid fixtures, runtime parity, satisfaction tests |
| Constrained table rows | Satisfied | valid fixtures, MIR rewrite test, runtime parity |
| Requirement normalization order and merge/conflict rules | Satisfied | binder tests and diagnostic inventory |
| Bind-once generic body law | Satisfied | dedicated open-body test hook plus undeclared-member rejection |
| Deterministic specialization identity independent of declaration order | Satisfied | identity-stability tests and hash-based naming law |
| Deterministic specialization reuse/deduplication | Satisfied | MIR tests, runtime parity, CLI repeatability |
| Resource limits | Satisfied | frontend diagnostics, inventory, and boundary tests |
| Generic-to-generic and recursion exclusion | Satisfied | diagnostics, fixtures, and runtime matrix exclusions |
| Closed MIR erasure | Satisfied | MIR assertions and backend leakage searches |
| C#/Diagnostic-JS/Symbolic-JS semantic parity | Satisfied | dedicated runtime/profile parity test |
| CLI fresh/stale artifact policy and byte determinism | Satisfied | focused CLI generic contract test with hashes |
| Closed generic result into existing TSON | Satisfied | valid fixture plus focused TSON plan identity test |
| Interface runtime/type storage values | Accepted-scope exclusion | excluded by M1b design |
| Inference/default arguments/generic nominal declarations | Accepted-scope exclusion | excluded by M1b design |
| General intersections/unions and new TSON behavior | Accepted-scope exclusion | excluded by M1b design |

This ledger has zero `Missing` rows for the bounded M1b feature as implemented in the repository.
