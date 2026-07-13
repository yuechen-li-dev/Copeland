# JTF-M4b foundational input contracts

## Contract and ownership

Platform callbacks stay in a concrete host. The host normalizes callback order into a `Machina.Runtime.Input.UiInputBatch`; it assigns the batch id locally and does not use timestamps or a global sequence generator. The batch is immutable and contains only typed records:

| Event | Semantics | Owner |
| --- | --- | --- |
| `UiPointerMoved`, `UiPointerButtonChanged`, `UiPointerWheel` | device position, transition, and wheel delta | Machina Runtime |
| `UiKeyChanged`, `UiTextEntered`, `UiModifiers` | device key state versus committed text | Machina Runtime |
| `UiSurfaceResized`, `UiCloseRequested` | neutral host lifecycle observation | Machina Runtime before explicit fan-out |
| `AurelianHostLifecycleInput` | latest host extent and close intent required by an engine frame | Aurelian Core |
| `MachinaFrontendCloseRequested` | frontend request, not a platform callback | Machina Presentation |
| `AurelianCloseRequest` | backend command accepted by Aurelian | Aurelian Core |

`Aurelian.Machina.AurelianHostInputTranslator` is the only bridge in this path. It maps a batch's last resize and any close request to the Aurelian-owned lifecycle value, and maps the frontend close message to the Aurelian close request. Pointer, keyboard, wheel, and text records are deliberately not copied into `AurelianFrameInput`.

## Event semantics

- Events retain the order in which one host iteration observed them; repeated events are valid.
- Pointer positions are absolute in the presented surface's coordinates. A movement may include the prior absolute point; it does not imply a relative-coordinate policy.
- A button record is a transition with an explicit button and pressed/released state.
- Positive wheel Y uses the current presenter convention: it moves content toward its origin, so scroll routing subtracts it from the scroll offset. X is preserved without assigning horizontal-scroll policy.
- Key transitions and text entry are different records. Text is non-empty committed text and is not inferred from keys.
- Resize is retained in the UI batch for coordinate/recomposition consumers. The engine receives only its last size through the bridge. A close request is sticky for the iteration.
- Empty batches are valid. Non-finite points/deltas and invalid sizes are rejected at contract construction.

Machina alone maps presented-image coordinates to logical viewport coordinates, including existing letterboxing, offsets, and outside-image handling. Aurelian receives no Machina logical coordinate.

## UI and playback

Machina routing remains responsible for hit testing, scroll routing, capture, focus, keyboard, and text behavior. The presenter sample's legacy event shape is a short compatibility adapter around `UiInputEvent`; its Avalonia normalization and TOML playback both convert through the same foundational contract before routing. TOML is an input artifact, never the canonical runtime representation.

## Frontend/backend boundary

Typed immutable C# records are the in-process transport. No JSON is emitted between in-process components. JSON is not currently warranted because no process, persistence, replay, or language boundary consumes these contracts; any future JSON representation must add an explicit schema version, stable discriminators, unknown-event behavior, and round-trip tests.

The close message is a minimal proof of the boundary:

```text
MachinaFrontendCloseRequested
  -> Aurelian.Machina translator
  -> AurelianCloseRequest
```

It does not pass a UI node, screen, callback, service, or platform event to Aurelian.

## Future InputMan seam and non-goals

InputMan may later consume `UiInputBatch` and produce action maps, rebindings, controller policy, and higher-level actions. M4b intentionally implements none of those capabilities, nor gamepads, touch, gestures, generalized event buses, internal JSON messaging, camera picking, renderer changes, or frame-loop policy changes.
