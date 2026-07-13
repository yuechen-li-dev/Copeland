# JTF-M4b input ownership reconciliation

## Type-and-flow inventory

| Existing type/path | Altitude | Producer -> consumer | Disposition |
| --- | --- | --- | --- |
| `PointerPoint`, `PresentedImageMapper`, `UiHitTestIndex` | Machina UI routing | presenter -> Machina hit testing | retained in Machina Runtime |
| `PresenterInputEvent` and related presenter enums | sample routing compatibility | Avalonia/TOML -> presenter router | retained temporarily as an adapter over `UiInputEvent` |
| `AvaloniaPresenterInputBackend` | platform normalization | Avalonia -> presenter sample | retained in host/sample; now constructs foundation records first |
| TOML playback input | artifact input | TOML -> presenter router | retained; converts through the same foundation adapter |
| `AurelianFrameInput` / `IAurelianFrameInputProvider` | engine frame | integration host -> Aurelian frame loop | retained; augmented only with optional Aurelian-owned lifecycle facts |
| `SilkNetFrameInputProvider` | platform/integration host | Silk window -> Aurelian frame loop | retained as host-owned current sample wiring; it does not introduce a platform type into Aurelian Core |
| Silk/Avalonia input types | platform | platform -> host | remain isolated in sample adapters |

The new canonical foundational vocabulary is in `Machina.Runtime.Input`. `AurelianHostLifecycleInput`, `AurelianHostExtent`, and `AurelianCloseRequest` are Aurelian-owned. Translation lives in `Aurelian.Machina`; no Aurelian subsystem references Machina types.

## Sample ownership follow-up

The visible-triangle project is physically under `samples/Integrations/Aurelian.VisibleTriangle`, is a `JointTaskForce.Integration.slnx` member, and is excluded from subsystem fast lanes. Its cross-system screen adapter and platform host are therefore reviewed as integration code. The dependency validator rejects the retired `samples/Aurelian/Aurelian.VisibleTriangle` location.

## M4c scope

JTF-M4c should complete presenter composition consolidation: move any remaining cross-system platform/sample adapters under `samples/Integrations` and retire the presenter compatibility event shape once the general Machina router directly consumes `UiInputBatch`. It should not add InputMan policy.
