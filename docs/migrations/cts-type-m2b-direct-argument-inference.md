# CTS-TYPE-M2b direct-argument inference ledger

| Status | Requirement | Evidence |
| --- | --- | --- |
| Satisfied | Local direct-argument inference | Binder uses ordered slots and a bounded non-recursive structural worklist. |
| Satisfied | Canonical exact agreement | Candidate equality uses the existing canonical type equivalence relation. |
| Satisfied | Contextual staging | Empty arrays, record literals, and bare Result constructors defer until a closed parameter type exists. |
| Satisfied | Explicit/inferred reuse | Both routes call the same closed-instantiation cache across MIR, C#, Diagnostic JavaScript, Symbolic JavaScript, and TSON. |
| Satisfied | Frontend resource limits | Depth 16, steps 128, and evidence entries 16 are diagnosed before MIR. |
| Satisfied | Generic-body exclusions | Existing generic-to-generic and recursion rejections are retained. |
| Stronger evidence | No inference leakage | Focused binder tests and emitted-source scans assert concrete MIR only. |
| Accepted-scope exclusion | Advanced inference | No partial explicit, return-context, overload, union, or backtracking inference. |
| Satisfied | Collision allocation | An internal deterministic hash seam proves sorted collision-group expansion from 16 to 24 hex characters; production also advances through 32, full digest, then escaped identity without throwing. |
| Satisfied | M2b evidence matrix | Focused fixtures now cover contextual empty-array/record success plus sole-evidence and constraint-only failure, a checked-in inferred-reuse MIR/C#/Diagnostic-JS/Symbolic-JS corpus is pinned by exact bytes and SHA-256 values, and CLI inferred-call emission remains pinned and repeatable. |

Missing rows: 0.

Recommended M2c scope: only if a genuinely separate, already-approved inference extension is selected later; do not use M2c as a catch-all for unrelated type-system work.
