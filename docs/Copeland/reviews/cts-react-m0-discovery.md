# CTS-REACT-M0 discovery record

## Outcome

CTS-REACT-M0 is **not complete** in this checkout.  The investigation reached
two explicit integration boundaries that make a truthful end-to-end React/CLR
browser claim impossible without beginning the deferred browser project-build
and TS-XML-profile work.

This is an honest stop rather than a partial React implementation.  Adding a
sample that copied a React bundle, hand-wrote JavaScript rendering, or computed
the JSON in JavaScript would conceal those boundaries and would not prove the
milestone's design claim.

## The intended architecture

The viable architecture remains deliberately split:

```text
Copeland TS / TS-XML application
├─ browser JavaScript
│  ├─ React and ReactDOM supplied by TSPack's materialized npm graph
│  ├─ Copeland reducer and typed browser dispatch
│  └─ typed HTTP/IPC client for one declared CLR operation
└─ .NET process
   ├─ generated C# from the CLR-facing Copeland module
   └─ System.Text.Json serializes a small declared DTO
```

React must render a snapshot supplied by Copeland dispatch; it must not become
the state store.  The first bridge operation should remain coarse and declared,
for example `SerializeState(count: int) -> string ! BridgeError`.  A browser
must not receive an operation name, CLR type name, or reflection handle from
untrusted page input.

## Evidence found

### What is already usable

* The native browser sample at
  `samples/copeland-ts/browser-dispatch` implements the intended reducer law:
  `state + typed event + pure reducer -> next state`.  Its JavaScript host
  retains one state value and does not inspect application fields.
* The JavaScript backend has a Browser runtime target and typed named host
  imports.  Existing browser dispatch and npm ESM-emission tests are green.
* CLR `using` is real compiler functionality.  The existing `ClrInteropTests`
  prove generated C# calls `System.Text.Json.JsonSerializer.Serialize`.
* `CSharpSidecarHost` already owns a bounded supervised child-process protocol,
  validates a compiler-owned handshake, limits UTF-8 frames, and kills a child
  process during disposal if graceful exit times out.  Existing process tests
  prove Node-side completion, remote failure, cancellation, and cleanup.
* TS-XML has an extension-selected parser and source spans; it is intentionally
  renderer-neutral today.

### Blocking seams

1. **TSPack's accepted `tscl` contract is Node-only.**
   `docs/tscl-tspack-m1.md` and the M1 fixture specify Node production output.
   In `TsclBuildContract.ReadRequest`, any
   `javascriptRuntime` other than `node` produces `COPE-TSCL-0005`; the
   launcher then awaits and logs an entry function instead of mounting a page.
   TSPack's own M1 note explicitly defers Vite/browser integration.

2. **TS-XML has no React semantic profile.**
   The current neutral binding deliberately reports `COPE-TSXML-0101` outside
   an explicit profile.  Lowering a `<main>` tree to React elements now would
   change the language contract; treating every `.tsx` file as React would
   violate the existing profile law and risk Machina/manifest regressions.

3. **The npm contract is deliberately function-only.**
   Copeland currently accepts only named npm function imports and flat transport
   values.  React's practical M0 surface needs a curated element/render
   contract: at least element construction, small props including a retained
   click callback, and a mount/render operation.  Interpreting `@types/react`
   is neither necessary nor supported, but an explicit React profile contract
   must be designed rather than smuggled through the generic sidecar contract.

4. **The existing sidecar direction is CLR-to-child-process, not
   browser-to-CLR.**
   It is useful lifecycle and protocol evidence, but it is not an HTTP/IPC
   listener reachable from a browser.  Reusing it without a browser-facing host
   would require the browser to launch or speak directly to a private child
   transport, which is prohibited by this milestone.

Because these seams are independent, a one-off React page cannot make the
result credible: even a successful manually bundled page would bypass the
TSPack/`tscl` contract and an unprofiled TS-XML path.

## Recommended next sequence

1. **TSPACK-TSCL-BROWSER-M1:** extend the project request and result contract
   from Node-only to an explicit browser ESM build.  TSPack should continue to
   resolve, lock, and materialize React/ReactDOM, then supply import-map or
   bundled browser realization metadata.  `tscl` should consume that data only;
   it must not scan `node_modules` or invoke npm.
2. **CTS-TSXML-REACT-M0:** add an opt-in React profile selected by project
   contracts, not by the `.tsx` suffix.  Start with a curated intrinsic set
   (`main`, `h1`, `p`, `pre`, `button`), text/children, and one typed `onClick`
   callback.  Lower to direct `React.createElement` calls or an equally narrow
   generated adapter.  Keep the current Machina and manifest profiles unchanged.
3. **CTS-BROWSER-CLR-BRIDGE-M0:** generate one declared browser client and a
   loopback-only .NET host endpoint for `SerializeState`.  Validate an `int`
   request and a string response, project failures to a stable `BridgeError`,
   and make startup/cleanup TSPack/application-host responsibilities.
4. Return to **CTS-REACT-M0** with a fixture that combines the three proven
   seams and drives an actual Chromium click.

The first two items can proceed independently.  The bridge contract should
reuse the current compiler-owned transport/metadata principles where practical,
but it should not expose the generic sidecar dispatcher to browser input.

## Proposed acceptance fixture after prerequisites

The eventual fixture should live beside existing Copeland samples and include
authored `App.tsx`, state/reducer source, a declared CLR operation, a minimal
host project, and a TSPack manifest.  Its visible sequence is:

```text
initial:     Count: 0   CLR JSON: {"message":"Hello from CLR","count":0}
after click: Count: 1   CLR JSON: {"message":"Hello from CLR","count":1}
```

The click must invoke the Copeland reducer first, then exactly one CLR bridge
operation for the next state, then React renders the resulting snapshot.  The
test must capture the response as coming from the .NET host, assert no
error-level browser console entries and no unhandled host exceptions, and
verify that the supervised host is gone after cleanup.

## Security and lifecycle constraints

* Bind the local proof host to loopback only.
* Generate an allow-list of operations; never dispatch by arbitrary browser
  strings or deserialize arbitrary CLR type names.
* Validate the small request/response shape before application code observes
  it.
* TSPack/application hosting starts and supervises the browser host and CLR
  process.  The browser never starts a local process.
* One state-update crossing is acceptable for M0; field-by-field CLR property
  traffic is not.

## Additional work performed

* Added this discovery record so the next milestones have the exact source
  boundaries, their intended ownership, and the non-negotiable security/lifecycle
  constraints in one place.
* No compiler, renderer, package-manager, or host code was changed.  This
  avoids widening the accepted Node-only TSPack contract or changing TS-XML
  semantics without the required profile design and cross-repository fixture.

## Validation

The following commands passed on 2026-07-27:

```text
dotnet build Copeland.slnx --no-restore

dotnet test tests/Copeland/Copeland.TS.Backend.CSharp.Tests/
  Copeland.TS.Backend.CSharp.Tests.csproj --no-build --filter
  "FullyQualifiedName~ClrInteropTests|FullyQualifiedName~SidecarProcessInteropTests|
  FullyQualifiedName~NpmSidecarExecutionTests"
  # 5 passed

dotnet test tests/Copeland/Copeland.TS.Tests/Copeland.TS.Tests.csproj
  --no-build --filter
  "FullyQualifiedName~TsXmlSyntaxTests|FullyQualifiedName~NpmInteropBindingTests"
  # 20 passed

dotnet test tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/
  Copeland.TS.Backend.JavaScript.Tests.csproj --no-build --filter
  "FullyQualifiedName~BrowserM0EmissionTests|FullyQualifiedName~NpmImportEmissionTests"
  # 9 passed
```

The required full command was also run:

```text
dotnet test Copeland.slnx --no-build
```

It completed with five unrelated pre-existing corpus-byte failures:

* `CallableCorpusTests.Callable_reference_corpus_is_byte_stable_in_all_emission_profiles`
  expected 1480 bytes, actual 1518.
* `CSharpCorpusTests.Pure_class_csharp_artifact_has_a_stable_hash`.
* `CSharpCorpusTests.Inferred_reuse_csharp_artifact_has_a_stable_hash`.
* `CSharpCorpusTests.Table_csharp_artifact_has_a_stable_hash`.
* `NominalUnionTests.Nominal_union_corpus_artifacts_have_stable_bytes_and_hashes`
  expected 1268 bytes, actual 1320.

All other test assemblies passed (C# backend: 237 passed; TS: 876 passed;
JavaScript backend: 168 passed; CLI: 47 passed; MSBuild: 8 passed; Markdown:
82 passed; authoring-food: 3 passed).  No source or corpus artifact involved in
those failures was modified for this discovery record.

No real React page or Chromium interaction is claimed: React and ReactDOM were
not present in the supplied TSPack materialization for this workspace, and the
Node-only `tscl` build contract cannot produce the required browser graph.

## Conscious deferrals

Full React API compatibility, `@types/react` ingestion, hooks, JSX namespace
compatibility, SSR/hydration, Vite/HMR product work, arbitrary CLR invocation,
callbacks from CLR into React, and a generalized sidecar/RPC framework remain
out of scope.
