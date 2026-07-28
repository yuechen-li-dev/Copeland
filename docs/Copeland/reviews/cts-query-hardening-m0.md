# CTS-QUERY-HARDENING-M0

## Result

The Copeland solution now has a green, repeatable canonical baseline.  A clean
build followed by `dotnet test Copeland.slnx --no-build` passed twice: 1,461
tests across eight test assemblies each time. `git diff --check` also passed.

This hardening round did not start the deferred query CLI, exported-function
ABI, or fallible dynamic aggregate work.

## Initial failure inventory and classification

The initial build-and-test run also emitted `MSB3026` copy retries because a
solution build overlapped live test hosts.  The affected process exited on its
own; no user process was killed.  A subsequent no-build suite isolated the
repeatable inventory below.

| Initial failing test(s) | Classification | Assessment |
| --- | --- | --- |
| `CSharpBackendTests.Pure_class_lowers_to_a_sealed_complete_carrier_and_static_functions`; three valid class/pipeline fixtures; class MIR/JS corpus checks | Real regression | The generic table-member probe bound `Person` before class-member dispatch, incorrectly reporting `COPE-CLASS-0005`. |
| `JavaScriptBackendTests.Async_if_control_flow_emits_explicit_state_transition`; `JavaScriptRuntimeTests.Node_executes_explicit_async_state_machine_in_both_profiles` | Brittle implementation-detail expectation | The explicit state machine remains present and executes; state is now the collision-safe `frame.__cope_state` slot. |
| Both real-local-package npm execution tests | Brittle implementation-detail expectation | The calls still execute with positional arguments; async parameters now reside in named frame slots. |
| Two callable corpus tests; pure-class JS corpus and symbolic corpus checks | Accepted deterministic output change with stale goldens/hashes | Callable host-carrier adaptation and retention helpers are now emitted. |
| `CSharpCorpusTests.CSharp_Corpus_Matches_Expected`, the table/pure-class/inferred-reuse C# hash checks, `TsonEncodeRuntimeTests.Table_m2_corpus_has_pinned_artifacts_and_repeated_canonical_fixed_point`, and the CLI tables-M2 test | Accepted deterministic output change with stale C# artifacts/hashes | `CopeColumn.Count` and concrete table/column count properties are canonical generated support. |
| `NominalUnionTests.Nominal_union_corpus_artifacts_have_stable_bytes_and_hashes` | Stale canonical artifact encoding | The checked-in C# golden had noncanonical line endings while the pinned length/hash already described the canonical output. |

No nondeterministic compiler output, environment/toolchain drift, unresolved
defect, or retained reusable-MSBuild-worker failure remained after isolation.

## Repair and prevention

`Binder.BindCall` now excludes class and enum namespaces from the table-member
probe.  Associated class calls are therefore dispatched by their intended
class-member path before a namespace value can be bound.  The existing C#
runtime compiler test and valid-fixture coverage now prove `Person.normalize`,
`Person.birthday`, and constructor use together.

The JavaScript checks now assert the new semantic generated shapes rather than
the retired slot spellings:

- async control flow uses `frame.__cope_state` and `frame.__parameter_*`;
- npm calls preserve ordered positional arguments through those slots;
- the existing Node tests execute both diagnostic and symbolic async paths;
- the table C# corpus test checks both `CopeColumn<T>.Count` and the concrete
  `_values.Length` implementation before enforcing its hash.

Each affected corpus emission was produced twice into isolated temporary files
and compared byte-for-byte before its golden was replaced.  The sources,
semantic/runtime checks, and reviewed output scopes were all held fixed.

## Golden and expectation changes

| Test/artifact | Old expectation | New canonical behavior | Root cause and prevention |
| --- | --- | --- | --- |
| `cts-call-m1/main.g.js`, `main.sym.js` | No host-carrier retention runtime; 9,029/7,560 bytes | Host carrier map and retained adaptation; 9,996/8,491 bytes | Accepted callable-host runtime change; corpus equality, SHA-256, and callable runtime tests cover it. |
| `cts-call-m0b/main.g.js`, `main.sym.js` | No host-carrier retention runtime; 1,546/1,508 bytes | Same helper contract; 2,513/2,439 bytes | Same accepted callable change and layered coverage. |
| `cts-call-m0b/main.g.cs`, `cts-class-m1/main.g.cs`, `cts-union-m0b/nominal-union.g.cs`, `m0-csharp-valid/inferred-reuse.g.cs` | Noncanonical CRLF byte artifacts despite canonical test metadata | Canonical LF artifacts matching the already-pinned generated lengths and SHA-256 values | Artifact-encoding drift; byte hashes now agree with corpus equality checks. |
| `cts-class-m1/main.g.js`, `main.sym.js` | No host-carrier runtime; 11,619/8,495 bytes | Carrier adaptation emitted; 12,758/9,426 bytes | Accepted callable runtime change; class compile/runtime and symbolic corpus tests cover it. |
| `m1-table-csharp-valid/empty-table.g.cs` | No `Count`; old SHA-256 `18326…D30` | `CopeColumn<T>.Count` and `_values.Length`; SHA-256 `D9C8291E…496369D` | Accepted table support change; new focused shape assertions plus hash. |
| `tables-m2/main.g.cs` | 34,774-byte C# artifact without count properties | 35,232-byte C# artifact with base, column, and table counts | Accepted table support change; compiler/runtime fixed-point and CLI execution coverage. |
| Async and npm generated-shape assertions | `frame.state` and unscoped `frame.<parameter>` | `frame.__cope_state` and `frame.__parameter_<name>` | Slot naming hardening; runtime execution and focused shape checks together prevent opaque hash-only regressions. |

The standalone-web build was also run as part of each solution build.  Its
embedded compiler payload was refreshed from the current compiler assemblies;
TSPack rebuilt the browser materialization deterministically and did not sweep
unrelated generated web assets into this change.

## Test isolation

The only lock reproduction occurred when compilation and tests were requested
in the same `dotnet test` invocation, allowing project builds to race loaded
test dependencies.  The required validation sequence isolates them:

```text
dotnet build Copeland.slnx --no-restore
dotnet test Copeland.slnx --no-build
```

The CLI integration tests already use unique GUID-based temporary directories,
dispose child processes, and remove only their owned directories.  Repeating
the no-build full suite showed no lock, stale output, or cross-test file
interference, so global parallelism was not disabled and no process-killing
workaround was introduced.

## Final validation

```text
dotnet build Copeland.slnx --no-restore     PASS
dotnet test Copeland.slnx --no-build        PASS (two consecutive runs)
git diff --check                            PASS
```

There are no remaining suite failures.  The repository has a trustworthy
canonical baseline for the bounded callable, async, npm, table, workbook,
query, React, browser, and standalone-web changes covered here.
