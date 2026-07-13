# JTF-M4d presenter, screen, and input closeout

The former host shortcut that represented a platform close as input-provider exhaustion has been retired. `SilkNetFrameInputProvider` now publishes an integration-owned immutable batch, routes frontend lifecycle messages, translates them through `Aurelian.Machina`, and carries the resulting `AurelianCloseRequest` to the Aurelian frame loop.

The engine accepts the backend-owned request at an explicit boundary. Acceptance stops the engine idempotently and the loop returns `AurelianFrameLoopStopReason.CloseRequested` without starting a further frame. Native disposal remains host-owned and occurs after the loop result.

The presenter router now has a staged overload for host resize handling: recomposition is performed at the resize event, so later pointer or wheel records in that same ordered batch use the new render geometry. Legacy presenter event compatibility shapes remain absent; live and playback input both use `UiInputBatch`.

Validation covers collector drain/order/empty publication, frontend translation, close acceptance and idempotence, no extra frame, and the integration host topology. JTF-M5 may now consolidate Dominatus ownership without reopening presenter, screen, or foundational-input policy.
