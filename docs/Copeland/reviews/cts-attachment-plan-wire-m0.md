# CTS-ATTACHMENT-PLAN-WIRE-M0

> HostAttachmentMir is compiler truth. The attachment-plan artifact is its versioned transport form.

Copeland emits `attachments.json` beside every `tscl` output. The artifact is
inspectable, ordered by attachment ID, and uses UTF-8 deterministic JSON. Its
SHA-256 is recorded in TSPack's `browser-materialization.json`.

The v1 envelope is `{ schemaVersion, projectId, plans }`. Each plan has stable
attachment, definition, instance, optional parent, host identity and semantic
selector, adapter, typed capability-name arrays, payload contract, optional
adapter payload, lifecycle booleans, and project-relative source provenance.
`null` is used for an unavailable payload; omitted fields are not equivalent to
`null`. A breaking envelope or field-law change requires a new schema version.
No absolute paths, DOM nodes, renderer roots, generated source, or arbitrary
compiler objects cross this boundary.

`TsclBuildContract` emits the artifact directly from bound canonical
`HostAttachmentMir`; it does not inspect generated React text or projected table
text. TSPack validates v1, required fields, lifecycle shape, and duplicate IDs,
then preserves the asset and writes an automatic browser loader. Future schema
versions fail with `COPE-ATTACHMENT-PLAN-1001`.

> TSPack delivers attachment plans. Application source does not reconstruct them.

The generated loader fetches `attachments.json` without `eval`, validates it
again, registers plans in deterministic parent-aware order, and waits through a
bounded `MutationObserver` for the emitted semantic host selector. It reports a
contextual missing-host diagnostic after five seconds and cancels pending work
on `shutdownAttachmentPlans`. Duplicate registration is idempotent only for an
identical plan; a changed payload updates a compatible mounted adapter, while an
adapter change unmounts before remounting.

> The browser runtime executes plans through adapters after their Copeland hosts become available.

The Custom Element adapter receives only `{ tagName, label }`, creates the
element itself, owns update/removal, and leaves its shadow tree private. React
attachments appear in the same neutral artifact with a null opaque payload in
M0; the existing application root remains its compatible bootstrap path.

The website's `Main.ts` starts only the React root and viewport state. It does
not enumerate attachment IDs, selectors, adapter IDs, payloads, or lifecycle
operations. The declarative renderer-host marker is compiler source and yields
three profile-specific Custom Element plans. Deferred: state/effect payload
generation, hot reload, SSR, hydration, portals, and async adapter loading.

## Semantic host replacement closure

Attachment identity belongs to the emitted semantic host selector, never to a
particular DOM node. The generated browser runtime records the current concrete
host privately with each mounted adapter root. One bounded `MutationObserver`
checks that the host remains connected, still matches the plan's semantic
selector, and still contains the adapter-owned root. If any condition fails
while the plan remains registered, the runtime unmounts the stale root, releases
its ownership record, and reuses the same plan and attachment ID through the
bounded host-readiness path. The latest registered compatible payload is used.

Plan removal remains permanent teardown; shutdown cancels all pending recovery;
and a replacement host which never appears reports contextual attachment,
component, adapter, and host diagnostics. Runtime inspection exposes counts and
state only, never DOM nodes or renderer roots. The browser proof includes a
React `createRoot` replacement of a stable semantic host and verifies two mounts,
one update, two unmounts, one live recovered attachment, then final cleanup.
