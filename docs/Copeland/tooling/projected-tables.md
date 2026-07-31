# Projected-table conventions

Projected relations are read-only inspection projections over one resolved
compiler-visible project context. They never re-bind source or own syntax.
The CLI owns query/formatting; `LayoutProjectedTableProvider` and related
providers own only the projection from canonical bound/normalized facts.

## Conventions

| Concern | Convention |
| --- | --- |
| IDs | Stable semantic IDs are strings; do not use DOM or display labels as IDs. |
| Parent IDs | `parent*Id` refers to a canonical semantic parent and is nullable only when the domain permits a root. |
| Definitions vs instances | Definition and instance columns are distinct; an attachment/frame/box must never collapse them. |
| Source | `sourceId`/provenance is project-relative authored source, never generated output. |
| Paths | project-relative `/` paths only; no absolute paths. |
| Order | explicit authored order where author order matters; canonical sort for artifact/projection determinism. |
| Time | compile-time facts and runtime observations are separate relations/columns; no mutable runtime object leaks. |
| Capabilities | named, sorted capability sets; no arbitrary JSON blob when a relation/value is available. |
| Versioning | artifact/table envelopes need declared schema versions before external compatibility is promised. |
| Mutability | all compiler-projected relations are read-only; mutation diagnostics must not imply a writable database. |

## Current relation families

- `layout::*`: layouts, boxes, bindings, collection items, derivations, and
  sources. Geometry is a projection; normalized derivation/provenance remains
  canonical.
- `text::*`: regions, documents, blocks, inlines, and bindings. The document
  tree is canonical; text tables are views.
- `component::*`: definitions, instances, bindings, captures, and local
  presentations. Instance IDs are semantic identities, not renderer roots.
- `renderer::*`: adapter contracts and attachment facts. Attachments derive
  from `HostAttachmentMir`, not emitted JavaScript.
- project/source provenance relations where materialized context is available.

Known inconsistency: several domain-specific providers use independently
constructed row objects. M1 should extract a small shared provenance/identity
row helper and schema-version convention, without creating a universal table
schema or moving domain-specific fields out of their owners.
