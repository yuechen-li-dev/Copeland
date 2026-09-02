# Copeland runtime scaffolding classification

## Purpose

This document classifies generated runtime structures by semantic ownership. It
is a deletion safety guide, not an optimizer design. A generated construct may
be verbose and still be load-bearing.

## Runtime law that must remain

| Structure | Semantic job | Safe optimization boundary |
|---|---|---|
| nominal type tokens | distinguish records, classes, enums, Results, tables, rows, and table columns that have the same visible shape | tokens may be passed to shared helpers, but identities must remain distinct and deterministic |
| record/class provenance | reject counterfeit carriers and preserve private-field authority | Diagnostic/Symbolic may use WeakSet membership and private Symbols; Production may use compact stable fields plus strict boundary validation |
| frozen or otherwise mutation-resistant values | enforce immutable record, class, enum, Result, row, column, and table behavior | a representation may change only if foreign JavaScript still cannot mutate a Copeland value observably |
| null-prototype or isolated carrier shape | prevent prototype behavior from entering Copeland values | retain in checked profiles; a compact Production representation must retain explicit own-property and type-token boundary checks |
| table, row, and column identity | prevent rows or columns from one table being accepted as another | implementation may share, identity may not |
| bounds classification | distinguish non-finite/non-integral indexes from finite out-of-range indexes and return the specified `TableBoundsError` | one shared bounds helper is valid if it returns the correct concrete Result and enum identities |
| concrete Result identity | preserve `T ! E` runtime validation and propagation law | validator implementation may share, but a value of one closed Result type must not counterfeit another |
| enum tag and payload validation | preserve exhaustive case and payload law | compiler-created values may use a trusted construction path; public/foreign values must still be checked |
| FLOW revision, reentrancy, guard, board, terminal, and result handling | preserve event-machine behavior | local emission cleanup is allowed; state-machine law is not |
| async terminal arbitration and continuation dispatch | preserve resolve/cancel/fail/panic exactly once | emit only when used, but do not replace it with naked Promise semantics |
| array bounds and batch order | preserve checked access and deterministic JavaScript batch order | helpers may share; no parallel JavaScript redesign follows |

## Compile-time facts that should erase

`template`, `static`, `reflect`, aliases, and interfaces are compile-time
facilities. They must leave no runtime code unless a template or reflection
query explicitly materializes a runtime declaration or artifact. Materialized
output is a root even when no authored runtime expression names it directly.

Compiler-private generated definitions may erase when a module-local
reachability walk proves they are not reachable from:

- module exports and public application entrypoints;
- top-level initializers with observable work;
- npm, JavaScript host, CLR, remote, and TSON boundaries;
- explicitly named boundary functions;
- generated module factories and materialized artifacts.

An authored function is not dead merely because the current corpus does not
call it. Library emission must conservatively root exports. Application/link
emission may later use stronger knowledge, but CTS-OPT-M0 does not add linker
semantics.

## Structures that may share

The preferred first scope is one emitted module or compilation unit.

- Table column construction and bounds handling may share a module-local helper
  when private storage, column identity, bounds-error identity, and concrete
  Result identity remain explicit inputs.
- Record, enum, and Result validator skeletons may share ordinary functions
  parameterized by immutable compiler-private descriptors. Per-type identity
  and exact diagnostics must remain.
- Result `ok`/`err` storage may share a representation only if closed Result
  identity remains independently validated.
- Error objects should remain allocated only on failure paths.

A global Copeland runtime library is not the first move. It adds runtime/compiler
version coupling, deployment coupling, and cross-realm identity questions. The
measured duplication must first survive a module-local experiment.

## Trusted internal construction

`JavaScriptEmissionProfile.Production` already establishes the right policy:
compiler-created record and enum values may use a compact trusted path, while
explicit boundary functions validate hostile values. CTS-OPT-M0 found that
Result/table lowering does not yet apply this policy consistently: the Tables
artifact retains 70 generated validator call sites in Production versus 79 in
Diagnostic.

The trusted path is valid only when the compiler emits both producer and
consumer and no foreign value can enter between them. Validation must remain at:

- exported or explicitly named boundary functions;
- npm and JavaScript host calls and callbacks;
- CLR or remote transport boundaries;
- runtime deserialization and TSON transport entrypoints;
- module factories callable by a host;
- any operation that accepts an untrusted carrier.

Internal trust does not mean removing nominal identity, bounds behavior, Result
identity, immutability, or exact error classification. It means not repeatedly
proving the same fact after a compiler-owned constructor has established it.

## Profiles and diagnostics

Diagnostic and Symbolic are checked representations intended for compiler
development and hostile-interop diagnosis. Production is already a release-like
representation; a second profile split is not justified. Future optimization
should finish the existing Production policy for tables and Results.

Generated JavaScript currently has no source-map artifact in the burn-in path.
Readable generated names and explicit staging are the source-correlation
surface. Definition DCE has low correlation cost. Temporary folding and helper
factoring have higher stack-trace and inspection cost and should retain stable,
deterministic names until source maps exist.

## Backend boundary

MIR contains typed definitions, statements, expressions, module exports, host
imports, table plans, flow definitions, and materialized artifacts. That is
enough semantic input for definition reachability and trusted/internal
classification. MIR does not need SSA.

Generated runtime helpers and validators are backend-owned symbols rather than
MIR definitions. The preferred verdict is `MIR_NEEDS_SMALL_ANALYSIS_METADATA`:
retain MIR, add a small deterministic backend emission graph or usage summary,
and perform module-local pruning/factoring before text rendering. Do not use a
regex or arbitrary post-emission JavaScript rewrite.
