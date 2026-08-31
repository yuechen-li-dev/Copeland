# Oblivion config/command dogfood — M19m

## Workflow

The built `oblivion` executable was used against a copied real M19i structured vault:

```text
oblivion config show
oblivion config get newline
oblivion config set newline preserve
oblivion config set appearance dark
oblivion config get appearance
oblivion config set appearance system

oblivion command list
oblivion command run workspace.reload -w <vault>
oblivion command run cards.expand-all -w <vault> --json
oblivion command run cards.collapse-all -w <vault> --json

oblivion card push artifacts/m19l/real-note.md -w <vault>
oblivion card content real-note -w <vault>
oblivion card pop -w <vault>
oblivion workspace validate -w <vault>
```

Config defaults were `system/preserve/default`; typed set/get round-tripped, and appearance was restored to `system`. Registry discovery returned the three intended descriptors. Reload succeeded through App. Expand-all reported two affected Cards and both IDs in session JSON. A separate collapse-all process correctly reported zero changes because commands do not control a prior process. Push/content/pop succeeded, final validation was one Page/two Cards with zero errors and warnings, and the Page SHA-256 was identical before and after (`451BF0D1DF0A5C87EBF93551C9AFA26A34D8A9180D74CACF4E30DA4459E6CE6D`).

## Boundary assessment

The three categories were natural in use: `card push` changed durable notebook content, `config set` changed persistent application policy, and `command run` performed an imperative process-local action.

| Question | Classification | Evidence / disposition |
| --- | --- | --- |
| Is `config` the right namespace? | `NO_FRICTION` | show/get/set reads naturally and contains only persistent policy |
| Is `command run` too verbose? | `MINOR` | explicitness is useful now; aliases should wait for repeated use |
| Are command IDs understandable? | `NO_FRICTION` | object/action IDs were clear and stable |
| Does config scope make sense? | `MINOR` | appearance is plausibly global; newline may eventually need authored-workspace scope evidence |
| Does newline belong globally? | `MINOR` | global default works, but collaborative vault conventions could justify a later workspace-owned override |
| Does appearance belong globally? | `NO_FRICTION` | it is application presentation policy, not workspace truth |
| Is card height ready? | `NO_FRICTION` | evidence says no; it remains derived/session layout and was not exposed |

Overall classification: `MINOR`, with three recorded minor observations and no repeated or blocking friction. Outcome A still applies: the split itself is clean, while M19n should validate live Standalone consumption rather than add more keys or commands.

## Recommended M19n

Add bounded Standalone consumption of the existing `appearance` contract: define honest system/light/dark palette selection at startup, prove no Workspace persistence and no runtime IPC, and reuse `OblivionConfigStore`. Do not add Settings UI, theme editing, configuration layering, command palette UI, or more command IDs. This recommendation is based on the only current contract-only key; newline and all three commands already have real consumers.
