# Copeland tables

Compiler-projected relations are read-only facts generated from immutable bound data. Layout exposes `layout::Layouts`, `layout::Boxes`, and `layout::Derivations` through the ordinary table tooling.

`layout::Derivations` contains one row per relative immutable row derivation: stable derivation ID, layout and target box IDs, transform, source box ID, normalized read/write field sets, authored order, resolution status, gap/padding, and project-relative source provenance. Mutation commands reject projected relations.

Nested `with` clauses and CSV `derivations` cells contribute identical rows. The source ID for a CSV derivation points to the individual transform call inside its parsed array cell, not to a synthetic CSV-only evaluator.

`layout::Boxes` retains resolved geometry and reports relative-derived fields through its normalized inspection constraints. The derivation row remains the canonical provenance; geometry is not merely replaced by a display string.

# Website fixture

The website's three stream roots project `layout::Layouts`, `layout::Boxes`,
`layout::Bindings`, `layout::CollectionItems`, `layout::Derivations`, and
`layout::Sources`. Its browser rectangle proof correlates those semantic host
names with the realized desktop, tablet, and mobile boxes. This is evidence for
layout tables, not a decision to make CSS presentation tabular.

# Manifest-aware projected inspection

A Copeland project has one compiler-visible world. Build, inspection, tables,
layout tooling, and the language server are different operations over that same
world. For a TSPack project, its normal manifest resolution writes a resolved
compiler-context descriptor beneath `.tspack/build-manifests`. The descriptor
contains source inclusion, project-relative module identities, package
contracts, browser/CLR backend contracts, and relevant compiler options. It is
read by compiler consumers; it does not start npm installation, a browser, or a
dev server.

Use an explicit project when possible:

```console
tscl table list --project ./manifest.tsx
tscl table schema layout::Boxes --project ./manifest.tsx
tscl table rows layout::Derivations --project ./manifest.tsx --format json
```

`--source ./src/App.tsx` searches upward for `manifest.tsx`. If it finds one,
the same materialized context is required and is selected only when it includes
that source. A source outside the context, a missing descriptor, and ambiguous
target contexts are diagnostics; inspection never falls back to a weaker source
scan. A source with no manifest remains the bounded source-only mode and has no
implicit package or backend contracts.

The JSON envelopes include `graphFingerprint`. It is a deterministic semantic
fingerprint over ordered logical sources and contents, package contracts,
runtime, and TSX profile; it deliberately excludes absolute paths and
timestamps. It lets table, layout, TSPack build, and editor tests assert that
they are using one compiler-visible graph.
