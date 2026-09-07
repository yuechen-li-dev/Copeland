# Bounded strategy profile

M18 adds four small reusable surfaces. Game truth remains in the application.

| Surface | Contract | Deliberate bounds |
| --- | --- | --- |
| `Aurelian.Strategy.EntitySelection<TId>` | Stable ordered identities; replace/add/subtract/toggle; detached snapshots; group store/recall and pruning | Eligibility, ownership, type and spatial query supplied by caller; groups 1–9 |
| `Aurelian.Strategy.VisibilityGrid` | Integer disk reveal, separate current visibility and persistent exploration; validated updates and detached exploration restore | 1–1024 cells per axis; no occlusion, faction policy, cadence or rendering |
| `Aurelian.GameWorld2D.IsometricProjection` | Validated diamond forward/inverse transform | Projection only; world positions and existing Camera2D remain separate |
| `Aurelian.Machina.StrategyHudProfile` | Typed resource/action/objective/selection facts plus style and captions become named Machina nodes | Logical 1280×800; maximum four resources/actions/objectives; host scales output |

`Aurelian.Strategy` has no game or graphics dependency. Snapshot arrays are detached; their contents are presentation/query values rather than a new authoritative entity store. Applications should supply deterministic identity comparison, eligibility and input meaning. A visibility grid belongs to the application/faction that owns it; it must call `Recompute` at its chosen semantic cadence. `RestoreExploration` intentionally clears current visibility until the application reveals again.

For the HUD, construct `StrategyHudSnapshot`, then call `StrategyHudProfile.Build(snapshot, style)`. `StrategyHudCopy` replaces objective/command/tactical labels and the control legend. Enabled action flags describe facts; they do not authorize an action. Resolve actions in the application even when a button is painted enabled. The current sample routes fixed logical action slots to typed intents; this is not a general reusable hit-routing layer.

Use `MachinaPresentationPipeline.Prepare(ui, width, height, loweringOptions)` when a host has a custom text measurer. M18 fixes forwarding of the existing `UiLoweringOptions`; the original three-argument overload remains compatible. The native sample uses the same Machina direct-outline font source for intrinsic measurement and raster realization.

## Integrated example

From the repository root, run:

```powershell
dotnet run --project samples/Integrations/Aurelian.StrategyDemo -c Release
```

The default command opens **Mossward**, a usable Avalonia window with four initial units, a crystal and wood loop, an objective, one constructible lodge, reserved production, visibility and a semantic minimap. It uses a native CPU-rendered bitmap presentation. This is not a Vulkan or large-army qualification.

- Click or drag selects; Shift toggles/adds; double-click selects the same unit kind.
- Right-click issues contextual move/gather/attack. B enters lodge placement; click commits; Escape cancels.
- N recruits a worker; F recruits a ranger; S stops; I selects an idle worker.
- Ctrl+1–3 stores a group; 1–3 recalls it. Space centers selection.
- Arrows/edge pan, wheel zoom, and minimap click move the camera. Focus loss clears held input and drag state.

Input flows through physical adapter → InputMan → typed intent → StrategySession. Dominatus maintains route/harvest/return phases and stages proposals; the session applies them in identity order. Aurelian CadenceScheduler drives 10 Hz semantic updates and the sample reveals at 2 Hz. Spatial2D owns overlap/sweep queries. The session owns costs, positions, production, cargo type and accepted construction.

Movement is a swept straight segment. If a building blocks it, the unit stops with a diagnostic; the player can issue a waypoint. There is no hidden pathfinder, unit separation, attack-move or rally implementation. The fixed map's water/trees are decorative except resource nodes used in placement checks.

## Proofs

```powershell
dotnet run --project samples/Integrations/Aurelian.StrategyDemo -c Release -- --proof
dotnet run --project samples/Integrations/Aurelian.StrategyDemo -c Release -- --launch-smoke
dotnet test tests/Aurelian/Aurelian.Strategy.Tests -c Release
```

The proof exercises real input mapping, group/focus behavior, preview isolation, rejected-build atomicity, gather/build/production completion, different cadence partitions, and detached in-memory intent replay. The launch smoke requires a visible window and three rendered frames before exiting. Its PNG is a framebuffer export, not a captured desktop screenshot.

No serialized save envelope is provided. Intent replay is an in-process sample diagnostic, retains its tape, and is not a production persistence/rollback protocol. See the milestone report for performance and qualification limits.
