# Avalonia and Skia role audit — M19

Classification:

| Use | Class | Decision |
| --- | --- | --- |
| Normal Mossward play | B — replaceable now | Default to Silk/Vulkan with native Profile, fog and Machina UI |
| Existing Mossward desktop implementation | A — compatibility host | Keep behind `--compatibility-avalonia` |
| Profile parity oracle | C — tool/test only | Keep sample-local `StrategyAssets` for objective native-vs-reference comparison |
| PNG evidence encoding | C — tool only | Retain; it does not define runtime composition |
| CPU/reference presentation tests | C — tool/test only | Retain deterministic raster validation |
| Mature desktop widgets/file dialogs | A — useful host capability | Use when a tool genuinely needs them |
| New in-game diagnostics | B — native Machina suffices | Prefer bounded development overlays/cards in the app |
| Wholesale package removal | D — not useful work | Explicitly rejected |

Normal command:

```powershell
dotnet run --project samples/Integrations/Aurelian.StrategyDemo -c Release
```

Compatibility command:

```powershell
dotnet run --project samples/Integrations/Aurelian.StrategyDemo -c Release -- --compatibility-avalonia
```

The policy is demotion by ownership, not a purge. The compatibility implementation remains available while the semantic center is Profile/Machina/Aurelian/Vulkan.

