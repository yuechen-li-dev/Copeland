# CTS-M6d: typed fallibility closeout migration

CTS-M6d closes the CTS-M4–M6 fallibility sequence. It adds no source syntax and no new runtime dependency.

## Changes

- Ratified the final Result, propagation, lexical recovery, and terminal unwrap laws in the canonical language profile and closeout architecture record.
- Added shared MIR validation for an invalid function-return propagation target, so C# and JavaScript reject malformed MIR uniformly and artifact-free.
- Added deterministic C#/Node parity coverage for success, inner and outer recovery, handler-to-function propagation, Result forwarding/match, and successful unwrap.
- Added host-instrumented Node proof that `?`, selected recovery, and successful `!` operands execute exactly once.
- Updated M4–M6 historical records so they no longer describe implemented JavaScript handlers as deferred or rejected.

## Compatibility and privacy

The source and MIR contracts are unchanged except that invalid hand-authored MIR now fails earlier. JavaScript flow records remain private branded control records; no source Result, enum value, plain object, or exported user API can stand in for one. Ordinary Result control flow still has no host exception mechanism. `COPE-PANIC-UNWRAP` remains terminal and backend-private apart from its stable classification.

## Validation ownership

The fallibility proofs remain owned by the Copeland TS frontend/MIR, C# backend, JavaScript backend, CLI, and their corpus fixtures. No shared infrastructure or Machina-owned source changes are required, so the slow Machina lane is not part of this migration.

The next product ladder should be separately named. Immutable nominal records are a reasonable candidate, but they are deliberately not designed or implemented here.
