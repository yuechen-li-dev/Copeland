# Browser runtime

The browser runtime realizes compiler-emitted facts; it does not define
Copeland semantics. For a browser project, Copeland emits JavaScript,
`attachments.json` v1, and (where applicable) a default-exported
`component-frames.js` envelope v1. TSPack validates/materializes these
artifacts and generates `@copeland/browser-v1`. The fixed browser runtime
executes frame envelopes; generated projects do not install a frame scheduler.
`registerComponentFrames` is browser-v1 compatibility-only for old artifacts;
the browser-v2 policy removes that side-effect path after maintained samples
and templates have shipped V1 output through a deprecation release.

The runtime owns semantic-host readiness and replacement recovery, attachment
registration, adapter lookup, Custom Element mount/update/unmount, component
frame registration, bounded event dispatch, traces, contextual diagnostics,
and shutdown. A concrete DOM element and an opaque renderer root remain
runtime-private.

The application owns only its app bootstrap. In the website dogfood sample,
React owns the outer application root while the compiler-generated runtime
owns Custom Element attachment lifecycle. An application must not recreate
attachment selectors, adapter choice, payload plans, or component instance IDs.

TSPack owns the canonical runtime source at
`tspack/cmd/tspack/runtime/browser-v1/index.js`; Go materializes and configures
it without reimplementing lifecycle semantics. Generated `dist/` output is
never a source-of-truth file. It is experimental browser infrastructure with
real Desktop/Tablet/Mobile proof coverage; it is not a general browser
framework, SSR/hydration implementation, or a promise of Vue/Svelte/Lit/Blazor
support.

For contracts and lifecycle details, see [generated artifacts](../reference/generated-artifacts.md)
and [semantic ownership](../architecture/semantic-ownership.md).
