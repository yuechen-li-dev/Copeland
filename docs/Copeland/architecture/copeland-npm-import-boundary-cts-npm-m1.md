# CTS-NPM-M1 npm import boundary

Copeland consumes an already-resolved, statically described, materialized npm dependency graph. It does not acquire packages, choose versions, write lockfiles, run lifecycle scripts, or inspect a package manager installation.

TSPack owns acquisition, installation, version selection, lockfiles, lifecycle scripts, workspaces, testing, templating, publishing, and advanced project policy.

The compiler projection contains only the package name, resolved version, materialization status and location, backend availability, and explicitly selected named-function contracts. A function contract records positional parameter types, result type, optional typed remote error, and whether the export is Promise-returning. It is intentionally not a TypeScript declaration parser.

After binding, each named import becomes module-owned metadata. MIR records package identity, resolved version, named export, authored local binding, and sync/async shape once. JavaScript emits native ESM imports from that metadata; it does not discover imports by traversing expression trees. CLR retains the same identity for the sidecar/TSON operation boundary.

The bounded call surface accepts zero or more positional primitive values, one-dimensional arrays of supported values, and declared flat immutable records, with primitive, one-dimensional-array, or flat-record results. Nested arrays and dynamic object shapes are rejected. Each position is checked against the selected static contract. A contract preserves whether its JavaScript export is synchronous or Promise-returning; the CLR sidecar remains asynchronous transport without changing that static identity. CLR lowering privately wraps the positional list in a compiler-generated nominal tuple for canonical TSON; JavaScript continues to emit `exportedFunction(arg0, arg1, ...)`. The wrapper is not an authored type or package contract.

Only named imports are in scope. Default imports, namespace imports, re-exports, callbacks, classes, constructors, arbitrary objects, overloads, iterators, streams, and package-management behavior are outside CTS-NPM-M1.
