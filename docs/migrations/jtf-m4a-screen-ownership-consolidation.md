# JTF-M4a — Presenter screen and layer ownership consolidation

## Before and after

Before M4a, `Aurelian.Core.Presentation.Screens` contained generic presenter stack, layer, visibility, and ordering mechanics. The visible-triangle sample made its world wrapper implement that Aurelian-owned presenter contract.

After M4a, the generic types live under `Machina.Presentation.Screens`; no `Aurelian.Core.Presentation.Screens` production namespace remains. `VisibleTriangleWorldScreen` retains only Aurelian sample/frame-loop behavior. The integration sample's `VisibleTriangleMachinaScreen` adapts it to Machina's screen contract.

| Prior type | M4a disposition |
| --- | --- |
| `IPresenterScreen` | Moved to Machina; now includes `PresenterScreenId` |
| `PresenterScreenStack` | Moved to Machina; existing ordering and visibility behavior retained |
| `ScreenLayerKey`, `ScreenLayerSlot`, `ScreenLayerOrder`, `Layer`, `ScreenLayers` | Moved to Machina |
| `VisibleTriangleWorldScreen` | Retained as Aurelian world/frame-loop sample behavior, without presenter metadata |
| `VisibleTriangleMachinaScreen` | New integration-sample adapter |

The only intentional public API addition is stable `PresenterScreenId`; duplicate identities are rejected after normalization. Screen contracts still do not return presentation content because the preceding abstraction had no such output and current callers do not need one.

## Visible-triangle and topology

The visible-triangle integration host configures background, world, HUD, overlay, debug, and cursor layers. Its world adapter is placed on `world`; tests prove mixed Machina UI/HUD/overlay ordering around that adapter. The triangle renderer, Vulkan path, frame-loop policy, and backend selection are unchanged. This is a structural/composition proof, not human pixel confirmation.

`Machina.Presenter.Sample`, `Machina.ComponentGallery.Sample`, and `Aurelian.VisibleTriangle` all compose Machina with Aurelian bridge/raster or graphics concerns. They are therefore members of `JointTaskForce.Integration.slnx`; neither Machina-only solution includes them. Cross-system visible-triangle tests now live in `tests/Integrations`.

## Enforcement and non-goals

The dependency validator now requires the generic screen declarations under `Machina.Presentation.Screens`, rejects the retired Aurelian screen namespace, verifies cross-system tests are integration-owned, and verifies cross-system rasterizing samples are integration-solution members and absent from Machina-only solutions. Existing project-reference allowlists continue to enforce the narrow `Aurelian.Machina` graph and both subsystem directions.

M4a does not redesign input, hit testing, focus, game commands, engine lifecycle, renderer contracts, raster behavior, Vulkan, shaders, navigation, transitions, or modal policy. M4b should reconcile raw-input and routing ownership using this finalized screen/layer boundary without expanding the contract.
