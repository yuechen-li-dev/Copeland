# Games

Games own their rules, authored agents, content, save schemas and proof scenarios. Aurelian owns reusable runtime services under `src/Aurelian` and platform integrations under `src/Integrations`.

| Game | Application | Launch |
| --- | --- | --- |
| Sunkill | `Sunkill/Aurelian.Ariadne.VnDemo` | `Play-Sunkill.cmd` |
| Beacon Run | `Beacon3D/Aurelian.Beacon3D` | `beacon3d.cmd` |
| Mossward strategy | `Strategy/Aurelian.StrategyDemo` | `dotnet run --project Games/Strategy/Aurelian.StrategyDemo -c Release` |
| TinyFarm | `TinyFarm/TinyFarm.Native` | `Play-TinyFarm.cmd` |
| Composable starter | `Starter/Aurelian.Starter` | `starter.cmd` |

Each game's tests live alongside its application. TinyFarm's CLI, playtesting adapter, Oblivion adapter and example scripts also live under its game directory. Project names and public namespaces are preserved.

See the [starter API guide](../docs/Aurelian/game-starter.md) for a small C# entry point, concept composition, customization and automated playtesting.

Use `starter.cmd --asset-demo` for the textured GLB example. The [static asset guide](../docs/Aurelian/static-assets.md) covers Blender authoring, materials, instances, delivery and explicit replacement.
