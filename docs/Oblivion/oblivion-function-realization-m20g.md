# Oblivion warm Function realization — M20g

## M20f baseline

M20f established the authoritative path: authored `.tsxtest` source is lowered by the existing Copeland MSBuild integration, materialized as an ordinary xUnit project, discovered by Test Platform, executed with an exact `FullyQualifiedName` filter, and projected from structured TRX into a Function Card. M20g changes only repeated setup around that path.

> Oblivion may cache knowledge of where/how to invoke the test. Oblivion never caches what the test returns.

## Realization and execution

`OblivionXunitFunctionRunner` owns a process-local dictionary keyed by the canonical owning project path. Each entry contains the deterministic realization fingerprint, materialized test project, resolved test assembly, and the complete Test Platform discovery list. Exact Card descriptors are selected from that retained discovery set; the selector still requires one exact base identity and never falls back to a similarly named test.

Every explicit Run creates a new GUID-scoped result directory, launches `dotnet test --no-build --no-restore` with the exact identity, reads the new TRX, records that GUID as `ResultIdentity`, and removes the temporary result directory. Outcomes, durations, failures, completion times, runtime effects, and TRX content are never cached.

## Fingerprint inputs

The `oblivion-function-realization-v1` fingerprint includes:

- the realization schema and `dotnet-test-trx-v1` runner identity;
- the owning project and recursively resolved `ProjectReference` projects;
- non-generated `.cs`, `.ts`, `.tsx`, `.tsxtest`, `.props`, and `.targets` inputs in each project directory;
- resolved file references with literal hint paths;
- applicable `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `NuGet.config`, and `global.json` files.

`bin`, `obj`, `.git`, and `artifacts` are excluded from source enumeration. This captures Function and production sources, compile item changes, project configuration, referenced compiler/tooling source, explicit binary references, target framework and package declarations without hashing the whole repository. The fingerprint is recomputed after a cold build before publication so restore-generated changes cannot cause a false second cold run.

## Cold and warm paths

Cold preflight resolves the source and owning project, fingerprints inputs, builds the owning project, resolves the single generated Copeland test project, builds it, validates the generated test assembly, runs Test Platform discovery, and publishes the realization only after all stages succeed. It then executes fresh xUnit and reads fresh TRX.

Warm preflight resolves and fingerprints again. If the project key and fingerprint match and both the generated project and assembly still exist, build and discovery are skipped. Exact descriptor selection uses the retained discovery list, after which fresh xUnit execution and TRX projection proceed normally.

The lock around project realization provides one build/discovery publisher at a time within the runner session. It is deliberately process-local and is not a general scheduler or persistent build cache.

## Inspection, invalidation, and failure behavior

Passive `card show` and UI inspection call `Inspect`, which reports an already session-realized descriptor when present but never hashes, builds, or discovers. Every Run performs the authoritative preflight.

Function/test source, production source, project/configuration, referenced project/tooling, package declaration, or explicit referenced binary changes alter the fingerprint and force cold realization. A missing test project or assembly also forces cold realization. A successful reload keeps the process-local entries; the next Run preflight selectively invalidates them from current inputs. No watcher is installed.

A failed rebuild or discovery never publishes the candidate and never executes the prior descriptor. A normal xUnit Failed result leaves the callable realization warm. A runner-host or TRX infrastructure error also leaves realization intact when the validated assembly and discovery inputs remain present; the following Run still performs its normal fingerprint/output preflight.

## Product surface

CLI JSON exposes `realization`, `realizationFingerprint`, invocation booleans, `resultIdentity`, and resolution, fingerprinting, materialization, discovery, execution, and total milliseconds. Human output adds one concise `Setup: cold|warm` line. The existing UI behavior remains unchanged; session execution results carry setup metadata without adding cache machinery to durable Card truth.

## Non-goals

M20g adds no persistent Test Platform host, result cache, durable realization state, watcher, Theory case UX, editor, artifact-output channel, broader executable runtime, reflection execution, or custom xUnit semantics.

