# Oblivion architecture and run guide

This guide points new contributors to the current product seams. Verify behavior in source and tests linked below; milestone reports retain evidence but are not the API index.

## Responsibility map

| Project | Responsibility | Start here |
| --- | --- | --- |
| `Oblivion.Model` | Workspace, page, card, content, and product contracts | [OblivionCardModel.cs](../../src/Oblivion/Oblivion.Model/OblivionCardModel.cs) |
| `Oblivion.Persistence` | Workspace loading/validation, vault files, and persistence/session models | [OblivionWorkspaceLoader.cs](../../src/Oblivion/Oblivion.Persistence/OblivionWorkspaceLoader.cs), [OblivionStructuredVault.cs](../../src/Oblivion/Oblivion.Persistence/OblivionStructuredVault.cs) |
| `Oblivion.Presentation` | Materializes product state into presentation data | [PresentationMaterializer.cs](../../src/Oblivion/Oblivion.Presentation/PresentationMaterializer.cs) |
| `Oblivion.UI` | Oblivion-specific declarations and interaction identities | [OblivionCardContract.cs](../../src/Oblivion/Oblivion.UI/OblivionCardContract.cs), [OblivionUiActions.cs](../../src/Oblivion/Oblivion.UI/Interaction/OblivionUiActions.cs) |
| `Oblivion.App` | Workspace/session operations, realization, command registry, and application use cases | [OblivionApplication.cs](../../src/Oblivion/Oblivion.App/OblivionApplication.cs), [OblivionCommandRegistry.cs](../../src/Oblivion/Oblivion.App/Application/OblivionCommandRegistry.cs) |
| `Oblivion.Cli` | `oblivion` command-line executable and JSON/text command surface | [OblivionCli.cs](../../src/Oblivion/Oblivion.Cli/OblivionCli.cs), [Program.cs](../../src/Oblivion/Oblivion.Cli/Program.cs) |
| `Oblivion.Standalone` and `Integrations/Oblivion.Avalonia` | Desktop host and Avalonia realization | [Oblivion.Standalone.csproj](../../src/Oblivion/Oblivion.Standalone/Oblivion.Standalone.csproj), [Oblivion.Avalonia.csproj](../../src/Integrations/Oblivion.Avalonia/Oblivion.Avalonia.csproj) |

The ownership boundary is: Oblivion state is truth; Machina View is a projection; layout is a realization; rendering is an output. Selection, expansion, scrolling, and render geometry are session or presentation state, not durable card content. See the [product contract](oblivion-product-contract-m18b.md) for the state taxonomy.

The CLI command tree (`Oblivion.Cli`) and the App command registry (`OblivionCommandRegistry`) are different surfaces. The CLI parses process invocations. The App registry exposes typed, process-local operations against a live application session; a fresh CLI process does not control an already-open desktop process.

## First CLI workflow

Run from the repository root in PowerShell. The CLI discovers a workspace in the current directory or its parents for workspace commands; pass `--workspace` to select one explicitly.

```powershell
$workspace = "src/Oblivion/Oblivion.Standalone/M19iNotebook.oblivion"

dotnet run --project src/Oblivion/Oblivion.Cli -- workspace validate --workspace $workspace
dotnet run --project src/Oblivion/Oblivion.Cli -- page list --workspace $workspace --json
dotnet run --project src/Oblivion/Oblivion.Cli -- card list --workspace $workspace
dotnet run --project src/Oblivion/Oblivion.Cli -- card show physical-atom --workspace $workspace --json
dotnet run --project src/Oblivion/Oblivion.Cli -- schema
```

The first command checks the vault, page listing exposes authored navigation, card listing and show inspect product content, and `schema` describes the agent-operable CLI surface as data. JSON output is available through `--json` or `--json-compact`. Command definitions and option behavior are in [OblivionCli.cs](../../src/Oblivion/Oblivion.Cli/OblivionCli.cs); CLI acceptance tests are in [Oblivion.Cli.Tests](../../tests/Oblivion/Oblivion.Cli.Tests/).

The CLI assembly is a separate executable project. `Oblivion.App` is a library and is reused by both CLI and desktop paths. Run `dotnet run --project src/Oblivion/Oblivion.Standalone` to open the desktop host; the standalone project carries fixture workspaces into its output. CLI and standalone test projects are under [tests/Oblivion](../../tests/Oblivion/).

## Editing and qualifying a workspace

Start with the structured notebook above or one of the [fixtures](README.md#current-authoring-examples). The workspace manifest connects pages and cards; card declarations refer to their content and artifacts. Edit the authored file that owns the value, then use `workspace validate` and the relevant `card`, `page`, or `function` command to observe product behavior. Do not edit generated build output as a substitute for workspace sources.

The CLI path is suitable for deterministic inspection and product operations. The desktop path is the route for visual reading and interaction. A successful CLI response does not prove the Avalonia desktop rendering or visual layout; use the standalone host and its tests for those behaviors.

## Test map

- [Model tests](../../tests/Oblivion/Oblivion.Model.Tests/) cover product records and model rules.
- [Persistence tests](../../tests/Oblivion/Oblivion.Persistence.Tests/) cover workspace and vault behavior.
- [Presentation tests](../../tests/Oblivion/Oblivion.Presentation.Tests/) cover materialization.
- [App tests](../../tests/Oblivion/Oblivion.App.Tests/) cover application operations and realization paths.
- [CLI tests](../../tests/Oblivion/Oblivion.Cli.Tests/) cover command parsing and result behavior.
- [Standalone tests](../../tests/Oblivion/Oblivion.Standalone.Tests/) cover desktop-facing surface contracts.

Use the repository's solution and test-lane guidance when choosing validation scope. The milestone reports link to their original, broader acceptance runs; their totals and timings are checkout-specific.
