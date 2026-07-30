# CTS-RENDERER-BOUNDARY-M0

## Decision

Copeland owns component definitions, instances, parent links, assigned hosts,
layout, DocumentMir, and application semantics. A renderer adapter only
realizes an already-bound presentation in a Copeland-owned host. Renderer node
identity is never Copeland component identity.

`ReactNode` stays as source compatibility only. `BoundComponentPresentation` is
the canonical result: presentation kind, adapter identity, content requirement,
required host facts, and opaque payload contract. Payload stays private.

## Audit matrix

| Concept | Neutral | React-specific | Browser-specific | Action |
| --- | --- | --- | --- | --- |
| Component definition/instance/parent link | yes | no | no | Retain as canonical identity. |
| Outer host/layout box | yes | no | DOM realization | Retain and project supplied capabilities. |
| `ReactNode` return | no | yes | no | Keep bridge; not component ontology. |
| TS-XML | parser | `react-m0` profile | no | Parser stays neutral; profile is adapter-specific. |
| Generated hosts | intended | `createElement` lowering | CSS/DOM | Current React realization; neutral MIR is next. |
| Root lifecycle | no | `createRoot`/`render`/`unmount` | yes | Isolate in React adapter. |
| Props/events | typed boundary | React convention | DOM events | Typed payload and callbacks stay explicit. |
| DocumentMir | yes | current element lowerer | semantic DOM | Content capability, never host capability. |
| Class/style | layout identity | `className` | CSS/DOM | Parent owns placement; child forwarding is unnecessary. |
| Portal/context | no | yes | sometimes | Private subtree detail; no cross-renderer portal. |

Remaining React-only machinery is `ReactRoot`, `ReactMountElement`,
`BoundReactElementExpression`, `MirReactElementExpression`, React TS-XML child
checking, and root member binding. These are not canonical semantics.

## Vocabulary

Host capabilities describe the parcel and lifecycle surface:

```text
ResolvedWidth, ResolvedHeight, FillAssignedBox, Clip,
ScrollX, ScrollY, Scroll, FocusContainer,
RendererAttachment, StableMountPoint
```

Content capabilities describe adapter realization:

```text
DocumentMir, SemanticText, InteractiveControls, ReactSubtree,
VueSubtree, SvelteComponent, CustomElement, Canvas, NativeMachina
```

## Contract and diagnostics

`RendererAdapterContract` holds identity, supported content capabilities,
required host capabilities, and browser applicability. Its law is:

```text
mount(instance, host, typed payload) -> private resource
update(same instance, resource, typed payload)
unmount(same instance, resource) -> release resource
```

One adapter owns one subtree. It cannot mutate parent geometry or sibling
internals. Props use a typed boundary; callbacks/effects are explicit; cleanup
is deterministic. There is no shared virtual DOM.

```text
COPE-RENDERER-0001 adapter unavailable
COPE-RENDERER-0002 unsupported content capability
COPE-RENDERER-0003 host missing required capability
```

`RendererAttachmentRegistry` implements `0004` duplicate host claim, `0005`
update/unmount after release, `0006` cleanup failure, and `0007` absent
canonical instance as a deterministic runtime-contract model. Browser/native
hosts can consume this model in M1 without global events.

## React and alternate proof

React-returning components normally get `RendererPayload` or `PrivateLayout`,
adapter `React`, `ReactSubtree`, and an opaque bridge contract. A direct
hyphenated Custom Element return is explicitly classified as adapter
`CustomElement` with `CustomElement` content capability, while retaining the
React source bridge for compatibility. The legacy `NativeMachina` kind only
means a private Machina layout; today it still crosses the React bridge.

The website routes React root create/update/unmount through
`ReactRendererAdapter.ts`; `Main.ts` retains application state. The explicit
unmount entry point is ready for the shared attachment registry's page cleanup.
React libraries retain props, callbacks, providers, styling, and
opaque descendants; Copeland never reads those trees.

The website's `copeland-renderer-badge` is a Custom Element with private shadow
DOM, placed in ordinary Machina FeatureCard grid hosts. It proves an alternate
browser-native opaque subtree without outer `className` forwarding. It is not
yet authored CustomElement adapter selection.

Vue will mount an isolated app root; Svelte a compiled component target; Lit a
Lit-managed element; Custom Elements one native element. They compose only at
Copeland hosts, never as mixed renderer VNodes.

Copeland's future state boundary is records, transitions, ordered effects,
flow decisions, and typed events. Renderers capture native events and retain
only visual ephemeral state:

```text
renderer event -> adapter callback -> Copeland callable -> transition/effect
```

`component::Definitions` projects presentation/adapter/requirements;
`component::Instances` projects mount identity and supplied hosts; and
`renderer::Adapters` projects contracts. LSP follow-up is hover/navigation for
these facts plus adapter diagnostics when authored selection lands. No
renderer-internal tree is projected. Next: neutral host-attachment MIR, authored
adapter selection, then native state/effects; no hooks/runes/store migration.
