# CTS-M4c JavaScript Result and propagation

CTS-M4c consumes the canonical CTS-M4b Result MIR in `Copeland.TS.Backend.JavaScript`. It deletes no MIR compatibility layer and does not restore retired call-local fallibility metadata: `MirCallExpression.Type` and `MirFunction.ReturnType` remain the authorities.

The JavaScript backend now accepts structural Result types, constructors, Result calls/parameters/locals, forwarding, explicit Result match, and `FunctionReturn` propagation. Unsupported Result equality and future Result forms still return precise backend diagnostics without an artifact. The generated private object layout, structural token deduplication, validator/panic path, unit payload choice, expression-prelude lowering, and parity proof are recorded in [the CTS-M4c backend note](../Copeland/architecture/copeland-ts-javascript-result-cts-m4c.md).

Validation includes the unchanged M1–M3 JavaScript corpus, exact LF corpus comparison and repeated emission, repeated Node execution, C# Result runtime regression coverage, CLI JavaScript emission, topology and dependency-boundary validation, and a bounded Node/Roslyn primitive-observable parity test. No Machina, Aurelian, integration, or slow-lane sources are touched, so those lanes are outside this compiler-local migration.
