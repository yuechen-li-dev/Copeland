# CTS-LAYOUT-INSPECT-M0

## Status

M0 exposes normalized layouts as compiler-projected, read-only Copeland tables. Layout inspection is a domain view over those tables, not a parallel table system.

> The authored layout tree is syntax. The normalized box table is compiler truth.

`tscl table list --source <entry.ts>` discovers `layout::Layouts`, `layout::Boxes`, `layout::Bindings`, `layout::CollectionItems`, and `layout::Sources`. `schema`, `rows`, and CSV `export` use the ordinary table command envelopes. These tables are deterministic and read-only: `set`, `add-row`, `delete-row`, and `import` diagnose that the originating layout/stream source must be edited instead.

`tscl layout inspect <layout|module::layout> --source <entry.ts>` resolves a concrete target and renders a filtered view over the same projected tables. Its JSON output is a standard table envelope containing the selected layout's relational rows.

The JSON schema has stable camel-case field names. `schemaVersion` is independent of the compiler version. Within a schema version, field meaning is stable and additions are optional; breaking changes require a new schema version.

Each box row reports its semantic path, parent identity, kind, constraint origins, typed dimensions, layer set/layer/rank, local z, authored order, normalized paint tuple, optional binding summary, and a foreign key to project-relative source rows. `fill`, `fit`, flow-derived locations, and host-unresolved facts remain typed constraints; the inspector never invents runtime pixels.

> The authored layout tree and CSV surface are syntax projections. The compiler-projected box tables are the shared semantic relation.

They’re the same picture.

Nested layouts, streams, and CSV-shaped layouts are different authoring projections over the same normalized relation. The command does not parse generated CSS, execute React, or inspect a browser. It also does not provide DOM measurement, intrinsic component bounds, a layout diff command, or `csv grid`.

Browser correlation remains backend-owned: a projected `Boxes.semanticPath`
splits into the generated host pair `data-machina-layout` plus
`data-machina-box`. Existing TSPack scenarios assert those hosts and
`elementFromPoint` paint ordering; the table provider has no browser dependency.

For ordinary diff tooling:

```console
tscl layout inspect DialogScene --source DialogScene.layout.ts --json > dialog.json
```

The fixture starts at `samples/copeland-ts/machina-layout-inspect-m0/01-fixed-overlay`.

> Your CSS framework was an Excel spreadsheet with a fan club.
