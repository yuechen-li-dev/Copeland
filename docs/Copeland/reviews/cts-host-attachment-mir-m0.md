# CTS-HOST-ATTACHMENT-MIR-M0

## Decision

Status: the compiler MIR, contract registry, lifecycle ownership model,
inspection, React-root dogfood, and the generated browser attachment executor
are implemented. Explicit source syntax for generic foreign-adapter selection
is implemented. The generated browser host owns Custom Element
mount/update/unmount through its attachment registry; React owns only the
empty outer host.

> A component presentation describes what should be attached. It does not directly invoke a renderer.

> Copeland owns attachment identity and host ownership. The adapter owns its private renderer subtree.

> Renderer selection may be inferred from typed content or authored explicitly when ambiguity exists.

> React, Custom Elements, Vue, Svelte, Lit, and native renderers are attachment strategies—not component ontologies.

`HostAttachmentMir` is the immutable compiler fact between
`BoundComponentPresentation` and a runtime adapter. Its deterministic identity
is `<component-instance-id>::attachment`; it records the definition, instance,
canonical parent instance, assigned Copeland host box, selected adapter,
separate host/content capability facts, payload contract, lifecycle policy, and
project provenance. It contains neither a DOM node, a React root, nor a
renderer payload value.

## Identity and selection law

Definitions name reusable source functions. Instances name authored calls.
Presentations describe the instance's renderer-facing content contract.
Attachments name the one assignment of that instance to its Copeland-owned
host. A renderer root is private adapter state and never replaces any of those
identities.

The current typed React bridge infers `React`; a direct typed hyphenated
Custom Element bridge infers `CustomElement`. Generic foreign payload uses
`ForeignComponent("Adapter", payload)`, for example
`ForeignComponent("CustomElement", <copeland-status-badge />)`. The adapter
literal is required: omission is an ambiguity diagnostic, an unknown literal is
unavailable, and a Custom Element payload must be one typed hyphenated element.
No package-name guessing occurs. The contract registry diagnoses unavailable
adapters, content incompatibility, host incompatibility, and payload-contract
mismatch.

## Registry and lifecycle

`RendererAdapterRegistry` is a deterministic, duplicate-rejecting registry of
adapter contracts only. `RendererAttachmentRegistry` owns active attachment
claims and invokes `IRendererAttachmentAdapter` mount/update/unmount methods.
The latter receives an immutable plan plus opaque payload/root values only.

The lifecycle is `unmounted -> mounted -> updates -> unmount -> released`.
Mount claims one host and one component instance. Update before mount or after
release diagnoses. A replacement adapter diagnoses: callers must explicitly
unmount then mount; cross-renderer subtree migration does not exist. Cleanup
failure retains the claim for retry/diagnosis rather than silently releasing a
possibly live renderer root. Parent teardown must request descendant attachment
unmounts deepest-first from the canonical instance graph.

Ordinary updates preserve component instance, attachment, host, and adapter
identity. The selected adapter reconciles its opaque payload. A host removal
is an unmount. An authored renderer-kind change is an explicit remount.

## Capability and event boundary

Planning verifies adapter-required host capabilities are a subset of supplied
host capabilities, and presentation content requirements are supported by the
selected adapter. Diagnostics include adapter, attachment/component identity,
host capability facts, and payload contract at the boundary.

Events remain bounded: `renderer event -> adapter -> canonical
component-instance callback`. Adapters remove subscriptions during unmount and
may not mutate layout rows. State, transitions, and effects are deliberately
deferred.

## Realizations and inspection

The React adapter keeps React root/context/provider/reconciliation private.
The Custom Element adapter owns element creation, property/attribute transfer,
update, removal, and its private shadow DOM. Both attach only inside assigned
Copeland hosts; siblings may use different adapters. Renderer-owned descendants
are not semantic Copeland children without explicit component instances.

`renderer::Attachments` projects immutable plans with identity, definition and
instance links, parent, host, adapter, capabilities, payload contract,
lifecycle policy, and project-relative source. `renderer::Adapters` also
projects accepted payload contracts. LSP component hover includes the selected
adapter and concrete attachment/host facts; compiler diagnostics continue to
flow through unsaved updates and clearing.

Vue can use an isolated application root, Svelte a compiled constructor, Lit a
managed element, and native Machina a direct native presentation plan. None
authorizes arbitrary mixed children inside a renderer-owned subtree.

## Deferred

This milestone does not introduce source hooks, state, runes, store
translation, effect scheduling, portals, SSR/hydration, a universal renderer
AST, or Vue/Svelte toolchains. The next bounded step is a Copeland-owned
component event/state record that targets a canonical instance attachment
without changing this boundary.
