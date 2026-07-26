# CTS-NPM-M1 module import metadata migration record

This record establishes a bounded compiler ownership boundary for npm interop. The production input is `CopelandNpmDependencyGraph`, which is projected from validated manifest IR through `CopelandNpmManifestProjection`. The older `NpmPackages` option remains a narrow test seam only.

The MIR program now owns deterministic npm import metadata. JavaScript imports are emitted from that list, sorted by package/export/local binding, with authored aliases preserved. Imports no longer come from an expression-tree scan. Duplicate local bindings are rejected by binding; names reserved for compiler helpers receive a deterministic diagnostic.

Diagnostics distinguish an undeclared package, a package without any static contract, a missing named export, unavailable materialization, and a compiler-helper name conflict. The projection remains read-only and never becomes a second package-management model.

The call-binding closeout replaced the historical single-request argument rule with contract-position binding: zero or more primitive, one-dimensional-array, or flat-record arguments and primitive, array, or flat-record results. Nested arrays and dynamic object shapes are rejected. Lowering carries both authored arguments (for JavaScript emission) and a compiler-private nominal argument tuple (for CLR TSON transport); the emitted operation identity remains package/version/export based. Contracts retain synchronous versus Promise-returning export shape, while the sidecar's asynchronous transport stays an implementation detail.
