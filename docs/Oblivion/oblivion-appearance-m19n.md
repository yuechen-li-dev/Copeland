# Oblivion standalone appearance wiring — M19n

## Outcome

M19n qualifies one standalone Oblivion product surface in `dark`, `light`, and `system` startup modes. The typed App-owned `OblivionConfig.Appearance` value is loaded from the existing `%APPDATA%\Oblivion\config.toml` contract. Standalone/Avalonia then resolves that semantic value to one of the two immutable `OblivionStandaloneStyle` instances before constructing the window.

`style = default` remains inert. Appearance selects the Light or Dark realization of that one style identity; there are no additional profiles, theme files, or live settings surfaces.

## Startup flow and system resolution

```text
OblivionConfigStore.Load
  -> OblivionConfig.Appearance
  -> light: Light
     dark: Dark
     system: Avalonia ActualThemeVariant at framework startup
  -> OblivionStandaloneStyles.For(resolvedAppearance)
  -> one standalone window, shell renderer, and content host
```

Avalonia's `ThemeVariant.Default` is requested for `system`, allowing the configured platform backend to supply the effective OS appearance. The application reads `ActualThemeVariant` after framework initialization, resolves Light or Dark, fixes the Fluent theme variant to that startup result, and logs configured/platform/resolved values. Explicit `light` and `dark` do not consult the platform result. Focused tests control the platform input and prove both system branches.

Invalid TOML and invalid appearance values remain failures of `OblivionConfigStore`; Standalone does not add a second parser or fallback.

## Dark source

`OblivionStandaloneStyles.Dark` is the M19g-M19m standalone palette without aesthetic changes: page `#050914`, Card `#0B1220`, restrained slate borders/badges, blue selected border, cyan square affordance, and the established dark mature reading surface. `M19h` remains a compatibility alias to `Dark` for existing geometry tests.

The final 2560x1440 dark proof was reviewed against the proven M19i expanded two-Card appearance. Card boundaries, selected border, document contrast, badges, expansion squares, and screen utilization remain stable.

## Light source and PoC reuse

`OblivionStandaloneStyles.Light` reuses the earlier PoC/Presenter Oblivion direction rather than inventing a new visual language: the existing light `StandardTheme` relationships, near-white Card/document surfaces, `#EDEFF0` application field, dark zinc text, restrained gray borders/badges, and the same blue selection accent. The standalone geometry and typography are inherited unchanged from Dark.

The final 2560x1440 light proof shows the same product and semantic state. It has readable dark headings/body text, subtle Card separation, a clear selected border, restrained badges, a coherent code surface, and no dark-only hosted-body or expansion-square residue.

## Shared style ownership audit

| Value owner | Classification | M19n treatment |
|---|---|---|
| viewport, margins, gaps, Card heights, prose cap | `STYLE_TOKEN` geometry | retained byte-for-byte in the shared style record |
| page, Card, border, selection, text, badges, hosted body, square affordance | `STYLE_TOKEN` appearance | centralized in `OblivionStandaloneStyle.Dark/Light` |
| mature document surface, text, headings, muted text, code, borders, links, diagnostics | `CONTENT_PRESENTER_STYLE` | adapted once into `AvaloniaOblivionContentStyle` and passed through the existing presenter |
| Fluent ScrollViewer/ScrollBar templates, focus/hover behavior, system theme signal | `PLATFORM_DEFAULT` | retained; the resolved Avalonia theme variant selects the matching control theme |
| dark hosted-body frame and dark 40px square literals | `ACCIDENTAL_HARDCODE` | replaced by optional renderer appearance inputs; existing non-Standalone callers retain their defaults |
| old content-host static dark brushes and white heading | `ACCIDENTAL_HARDCODE` | replaced by one supplied mature-content style |

The remaining dark preview/source-label constants in `OblivionCardRenderer` belong to older body-preview paths and are not active in Standalone (`RenderBodyContent: false`, no source label). They were not refactored because M19n does not redesign unrelated content paths.

## Shell and mature content integration

Both appearances use the same `OblivionStandaloneSurface`, `OblivionStandaloneRenderer`, `OblivionCardRenderer`, normal `VStack`, Avalonia window, and `AvaloniaOblivionContentHost`. Palette values differ; control trees and layout do not.

The mature Markdown presenter receives the resolved document palette for its outer surface, body, headings, lists, quotes, links, inline code, fenced code, diagnostics, and border. The same read-only code presenter therefore remains readable in both themes without syntax-highlighting or editing changes. ScrollViewer structure and wheel ownership are unchanged. Fluent light/dark control themes keep the vertical thumb and track coherent with the resolved appearance.

Selection remains a two-pixel blue border only. Badges remain the existing compact secondary badges using palette-owned surface, foreground, and border. The 40px square remains filled when collapsed and outlined when expanded; only its surface/accent colors vary.

## Geometry, state, and persistence

Focused parity tests render the real one-Page/two-Markdown-Card structured vault in both appearances and require identical dimensions, Page extent, Card order, Card rectangles, expansion-square rectangles, selection, and expansion state. The visual proofs use both Cards expanded, Card A selected, Page at top, and the same 2560x1440 capture path.

No workspace, Page, Card, Markdown, session, stack mutation, reload, or vault-persistence model changed. Appearance remains application config only. MULTI_EXPAND, selection semantics, the 24px gap, 88px/72px margins, 174px/760px heights, 1040px prose cap, and page/document wheel routing remain unchanged.

## Visual proof

- `artifacts/m19n/standalone-dark.png`
- `artifacts/m19n/standalone-light.png`

Both files are 2560x1440. Review found no obvious low-contrast text, invisible selection, unreadable badge, dark-only light-mode surface, washed-out dark surface, geometry drift, or scrollbar regression.

## Validation

`JointTaskForce.slnx`, `Copeland.slnx`, `Oblivion.slnx`, `Machina.UI.slnx`, `Machina.UI.Slow.slnx`, the no-restore Machina build, the no-build Aurelian lane, and the affected Presenter sample build all pass. The canonical playback report contains 14 passed, zero failed, and zero skipped scenarios. The real standalone dark, light, and system capture launches all complete; system resolved the current Avalonia platform signal to Light. `git diff --check` passes, and config was restored to `appearance = "system"`.

## Non-goals

M19n adds no Settings UI, command palette, live observer, theme editor, custom theme, workspace/Page/Card appearance, style preset, layout option, content kind, browser, editor, agent, execution, watcher, IPC, or pixel-golden test.
