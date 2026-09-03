# Aurelian game architecture ground report

Status: historical baseline, superseded as current architecture by AURELIAN-CHKPT-M0.

The original AURELIAN-GAME-M0 audit established the pre-TinyFarm integration ground: Dominatus policy/runtime, Aurelian world/render foundations, Machina.UI capabilities, renderer-leaf ownership, and missing application state/resolver/persistence work. TinyFarm M1–M21 subsequently implemented and qualified those missing application paths.

For current architecture, read:

- `docs/Aurelian/aurelian-engine-architecture-v1.md` — authoritative engine/application architecture;
- `docs/Machina.UI/machina-renderer-neutral-presentation-architecture.md` — current UI/backend boundary and Avalonia feasibility;
- `docs/Aurelian/games/tiny-farm-m1-m21-consolidation-report.md` — milestone-to-current evidence bridge;
- `docs/Aurelian/games/aurelian-game-m0-integration-audit.md` — original detailed ground audit, retained as history.

The current ownership summary is:

```text
TinyFarm Core          owns game truth, concrete intents, resolution, events, hash
TinyFarm Runtime       owns session, content loading, persistence, navigation, host, DTOs
Dominatus              owns agent flow and decision policy
Copeland/TSON          owns authored semantic table/program truth
Machina.UI             owns semantic UI, layout, interaction, input records, presentation IR
Aurelian               owns reusable world/runtime/render systems and the formal engine role
presentation adapters  own backend/window/device realization only
```

No current document should infer that Aurelian is only a Vulkan renderer, that Dominatus owns game state, that raw TSON tables are runtime authority, or that Machina/Avalonia owns gameplay state.
