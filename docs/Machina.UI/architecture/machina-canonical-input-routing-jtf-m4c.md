# Machina canonical input routing (JTF-M4c)

`UiInputBatch` is the only general Machina input seam. Platform adapters and playback artifacts construct an ordered immutable batch; Machina then routes that batch without platform or Aurelian references.

```text
platform callback or TOML playback
  -> UiInputBatch
  -> Machina frontend/input routing
  -> UI actions, frontend messages, recomposition requirement
```

`MachinaFrontendInputRouter.Route(UiInputBatch)` is the lifecycle route. It preserves event order, records every resize, emits one `MachinaFrontendCloseRequested` for each close observation, and does not stop later events in the same batch. A resize requires recomposition; the integration host applies its existing constrained-surface and letterbox policy before routing later batches.

Presenter-specific action routing is integration code in `samples/Integrations/Machina.Presenter.Sample`. `PresenterUiInputRouter.Route` also accepts `UiInputBatch`, processes events sequentially, and returns typed routed actions, interaction state, frontend messages, and a recomposition flag. Its pointer, wheel, keyboard, text, nested-scroll, hit-test, and capture helpers consume foundational records directly. There is no presenter event union or foundational-to-presenter compatibility conversion.

The integration composition root retains the current presentation/routing artifact, routes a batch, applies resolved UI actions, recomposes when requested, translates `MachinaPresentationFrame` through `Aurelian.Machina`, and selects the Aurelian backend. Machina does not select a backend; Aurelian does not consume `UiInputBatch`.

Close uses the explicit contract:

```text
UiCloseRequested
  -> MachinaFrontendCloseRequested
  -> Aurelian.Machina.AurelianHostInputTranslator
  -> AurelianCloseRequest
```

The bridge maps typed frontend resize messages into Aurelian lifecycle input. The integration host converts a frontend close message to the Aurelian-owned close request, then applies the engine's current close lifecycle behavior.

TOML remains a linear playback artifact. It parses into foundational input records and has no runtime event semantics after batch construction. Key transitions and committed text are distinct records. InputMan, action maps, rebinding, controller/touch input, IME composition, gestures, gameplay policy, and JSON messaging remain future work.
