# Diagnostics overview (M1)

Copeland diagnostics are deterministic and ID-based. Common families:

- `COPE-PARSE-*`: parser syntax recovery diagnostics
- `COPE-BIND-*`: name/symbol binding diagnostics
- `COPE-TYPE-*`: type checking diagnostics
- `COPE-PROFILE-*`: Browser TypeScript language-profile constraints
- `COPE-ENUM-*`: enum declaration/construction diagnostics
- `COPE-MATCH-*`: match analysis diagnostics
- `COPE-CS-*`: C# backend diagnostics
- `COPE-CLI-*`: CLI command/usage diagnostics
- `COPE-TRY-*`: typed lexical `try`/`except` shape, type, target, and bounded-block diagnostics
- `COPE-CLR-*`: CLR namespace/type binding, member lookup, overload selection, and bounded projection diagnostics

## Key M1 profile and hardening diagnostics

- `COPE-PROFILE-0001` — `var` is not supported.
- `COPE-PROFILE-0003` — `eval` is not supported.
- `COPE-PROFILE-0005` — `null` is not supported.
- `COPE-PROFILE-0007` — ternary `?:` is not supported; use `if` expressions.
- `COPE-PROFILE-0008` — optional chaining `?.` is not supported.
- `COPE-TYPE-0012` — fallible call must be handled (for example with `?`).
- `COPE-TYPE-0017` — `if` condition must be `boolean`.
- `COPE-TYPE-0018` — `if` branch types must agree.
- `COPE-TYPE-0004` — type mismatch in assignment/return/argument contexts.
- `COPE-MATCH-0004` — non-exhaustive match.
- `COPE-MATCH-0005` — enum payload arity mismatch in a match arm.
- `COPE-MATCH-0007` — match arm expression type mismatch.
- `COPE-ENUM-0007`/`COPE-ENUM-0008` — enum constructor misuse.
- `COPE-TRY-0001` through `COPE-TRY-0006` — malformed handler shape, protected/handler value mismatch, incompatible handler error sites, missing targeted propagation, unsupported value-block control flow, and invalid handler binding.
- `COPE-CLR-0001` through `COPE-CLR-0010` — unavailable CLR namespace/type, imported-type ambiguity, inaccessible or missing member, no applicable/ambiguous overload, unsupported shape, deferred async disposal, local/import conflicts, and invalid directive placement.
