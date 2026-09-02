# Generated-definition reachability — CTS-OPT-M2

CTS-OPT-M2 adds a small JavaScript-emitter graph between validated MIR and final
text assembly. It removes only compiler-owned top-level definition blocks. It
does not remove authored functions, rewrite surviving bodies, parse emitted
JavaScript, or add a linker/SSA/CFG optimization layer.

## Analysis layer and definition model

The analysis lives in `Copeland.TS.Backend.JavaScript`, after the existing
`MirValidator.Validate` and backend validation steps have accepted the complete
program. This is the narrowest correct layer because MIR has already selected
closed specializations and concrete nominal types, while the emitter knows the
actual helper definitions and references that each profile will produce.

Each registered definition has:

```text
stable id
kind
ordered emission block
semantic root flag
generated-definition references
emitted UTF-8 byte count
```

The first qualified categories are record/class carrier families,
record/class validators, and closed Result validators. These are the categories
for which CTS-OPT-M0 had exact dead-region evidence. Table, enum, FLOW, async,
callable, TSON, and interop scaffolds remain conservative roots unless they
reference one of these registered definitions. Extending registration to a new
category requires its own structural ownership and runtime proof; M2 does not
guess from names.

Record carrier IDs are `record:<MirRecordTypeId>:carrier`; validators use
`record:<MirRecordTypeId>:validator`. Result validator IDs recursively encode
the stable MIR component type identities. IDs are fixed before marking, do not
depend on object hashes, dictionary order, or the surviving-definition index,
and do not change any runtime token or nominal identity.

## Roots and references

The root taxonomy is explicit even where the current emitter conservatively
maps several classes to the same operation: a reference made outside a
registered generated-definition block is a semantic root. Applicable root
classes are:

- `EXPORTED_SOURCE_API` and `PUBLIC_FACTORY`: project exports and generated
  module factories call the registered carrier/validator directly.
- `PROGRAM_ENTRYPOINT` and `TEST_GENERATED_ENTRYPOINT`: authored functions and
  test entrypoints remain emitted; their generated references are roots.
- `TOP_LEVEL_INITIALIZER` and `REQUIRED_TYPE_IDENTITY`: table constants, nominal
  tokens, singleton construction, and other retained top-level scaffolds root
  every registered dependency they use.
- `HOST_INTEROP_CONTRACT`, `NPM_INTEROP_CONTRACT`, and `CLR_FOREIGN_BOUNDARY`:
  explicit JavaScript boundary functions and host/npm adapters retain parameter
  validators. CLR emission is unchanged.
- `MATERIALIZED_TEMPLATE_ARTIFACT` and `TSON_RUNTIME_ROOT`: materialized runtime
  MIR and demand-created TSON plans remain emitted and root their dependencies.

Compile-time `reflect` creates no runtime root. Initializer-bearing categories
that have not been proven pure are not registered for deletion. This keeps
initialization, registration, diagnostics, and foreign-boundary behavior
conservative.

References are recorded when typed emitter accessors select a carrier,
validator, constructor, token, provenance set, or field slot. The writer tags
each structured line event with its owning definition. No emitted source is
searched or reparsed.

## Marking and emission filtering

Roots enter a stack in stable-ID order. The ordinary visited-set traversal
visits each definition's references in stable-ID order. Consequently a dead
self-reference or strongly connected component remains dead, while one edge
from a root retains the complete cycle. A shared dependency remains while any
reachable consumer references it.

After marking, the writer assembles only reachable definition-owned line
events, preserving the original order of every survivor. Generated name
allocation occurs before filtering, so removing an earlier definition cannot
rename or renumber a later semantic identity. The public reachability report
records the full graph, retained/removed counts, and exact block bytes. Setting
`EnableGeneratedDefinitionReachability` to `false` emits the same registered
blocks and provides the controlled baseline used by the dogfood harness.

## Profiles, validation, and mapping

Diagnostic, Symbolic, and Production use the same semantic graph; profile
differences naturally create different reference edges. Production is the
optimization evidence profile, while updated Diagnostic/Symbolic corpus
artifacts prove deterministic checked output.

MIR validation precedes graph construction. A dedicated malformed-MIR test
requires a `COPE-JS-0002` failure and a null reachability report, so invalid dead
MIR cannot hide behind filtering.

The JavaScript backend currently emits no source-map artifact. M2 changes no
source-map or authored-location contract and filters complete line-event blocks,
so it cannot leave a partial mapping entry. Existing `.tsxtest` authored
mapping is owned by the MSBuild/C# test path and is unchanged. JavaScript stack
line numbers may become smaller because dead top-level blocks are absent; there
was no authored JavaScript source mapping to preserve or claim.

## C# applicability and non-goals

C# source sizes are recorded by the measurement harness, but the pass is not
applied there. Roslyn/JIT already owns downstream code elimination, and forcing
JavaScript's emitter-private block graph into C# would not fall out for free.

M2 adds no whole-program linking, arbitrary authored-function liveness,
expression/local DCE, constant propagation, branch folding, inlining, or
post-emission textual rewriting. The existing project emitter's historical
module assembly is outside this milestone; M2 itself never searches generated
JavaScript.
