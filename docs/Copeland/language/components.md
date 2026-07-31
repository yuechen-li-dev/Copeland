# Components, state, and renderer boundaries

Components are ordinary typed functions that return a presentation value. A
call placed in a stream creates a compiler-owned component instance with a
stable identity and an assigned outer host. The component may capture typed
arguments and use a private local stream; neither a React root nor an adapter
subtree becomes a component identity.

The following shape is exercised by `ComponentCapsuleM0Tests`:

```ts
record CardProps { title: string; }

function FeatureCard(props: CardProps): ReactNode {
    return <article>{props.title}</article>;
}

stream Page<0px, 0px> {
    width: 600px;
    height: 240px;
    content: FeatureCard({ title: "First" }) { height: fill; }
}
```

The binder owns definition, instance, capture, and parent-host identity.
`component::*` relations and LSP hover inspect those facts. C# and JavaScript
consume them; a renderer adapter only realizes the selected attachment.

## State and events

State is component-local. A stateful component uses `state` and `on`:

```ts
function Counter(): ReactNode {
    state current: CounterState = { count: 0 };
    on Increment() => current with { count: current.count + 1 };
    return <span>{current.count}</span>;
}
```

The binder owns state/event meaning and presentation branches. The C# semantic
runtime proves typed transitions, effects, ordered completion phases, and
deepest-first attachment release. Browser component frames currently support a
smaller, compiler-emitted zero-payload transition subset; do not infer browser
support for every typed effect or event shape from the C# runtime model.

Effects have a compiler/runtime foundation (`effect`, optional completion, and
completion phases) but browser effect execution remains deferred.

## Renderer interop

React and Custom Elements are proven attachment strategies. Attachment plans
are compiler facts (`HostAttachmentMir`) projected to `attachments.json`; they
select host, adapter, capabilities, and payload contract. The runtime realizes
the plan only after its semantic host exists. Applications should not enumerate
the plan or manually attach compiler-owned renderer children.

See [browser runtime](browser-runtime.md), [feature status](../reference/feature-status.md),
and the [semantic ownership map](../architecture/semantic-ownership.md).
