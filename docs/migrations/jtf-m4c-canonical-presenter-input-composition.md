# JTF-M4c canonical presenter input and composition

## Retired compatibility path

| Retired item | Former producer -> consumer | Replacement | Deletion point |
| --- | --- | --- | --- |
| `PresenterInputEvent` and `PresenterInputKind` | Avalonia/TOML -> presenter router | `UiInputEvent` in `UiInputBatch` | `PresenterInputModels.cs` |
| `PresenterInputButton` | presenter transition wrapper | `UiPointerButton` | `PresenterInputModels.cs` |
| `PresenterKey`, `PresenterKeyModifiers`, `PresenterKeyboardInput` | presenter keyboard wrapper | `UiKeyChanged`, `UiModifiers`, `UiTextEntered` | `PresenterInputModels.cs` |
| `PresenterInputPoint` | presenter coordinate wrapper | `PointerPoint` | `PresenterInputModels.cs` |
| foundational-to-presenter adapter | Avalonia/playback compatibility conversion | direct foundational construction | Avalonia adapter and playback runner |

The presenter sample moved from `samples/Machina.UI` to `samples/Integrations` because it composes Machina presentation, `Aurelian.Machina`, an Aurelian raster backend, and Avalonia. It is now excluded from the Joint Task Force fast solution and belongs to the integration solution.

## Canonical behavior

Both live Avalonia callbacks and TOML playback now build foundational records and call the same batch router. Batch order is never sorted, timestamp-reordered, or coalesced. Pointer movement precedes any subsequent press in the same sequence; wheel routing uses the current hit-test and nested scroll state; repeated close records emit repeated frontend messages; events after close are still observed deterministically.

Resize remains a frontend observation. Machina emits a typed frontend resize message and recomposition requirement; hosts preserve their existing constrained surface, aspect ratio, and letterbox behavior. Aurelian receives a translated host extent only through `Aurelian.Machina`.

## Validation and remaining M4d closeout

The dependency validator rejects retired presenter compatibility tokens, missing `UiInputBatch` routing seams, a presenter router outside integration ownership, a direct raw-close-to-Aurelian lifecycle conversion, and the old presenter sample location.

M4d should close out only proven host-composition gaps: unify per-event host batching where a host can deliver a multi-event callback iteration, add focused integration proofs for resize-before-pointer and playback/live parity against the composed presenter, and decide whether engine close-command acceptance should become a first-class frame-loop command seam. It should not add InputMan, bindings, controller support, or new UI behavior.
