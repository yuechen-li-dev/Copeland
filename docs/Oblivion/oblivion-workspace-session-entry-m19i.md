# Oblivion workspace/session entry — M19i

## Vault open and materialization

The standalone executable accepts an explicit `--vault <root>` option. Its default is one fixed deployment-relative directory, `M19iNotebook.oblivion`; it does not search for a plausible workspace.

The real entry path is:

```text
explicit vault root
  -> OblivionWorkspaceLoader.OpenVault
  -> format-1 JSON/TOML/Markdown validation
  -> Oblivion.Model workspace, Page, and Cards
  -> OblivionApplication.OpenWorkspace
  -> OblivionWorkspaceSession
  -> OblivionStandaloneSurface
  -> unchanged M19h renderer and Avalonia document host
```

`OblivionApplication.OpenWorkspace` selects the declared default Page, reconciles a fresh `OblivionSessionState`, and returns the loaded workspace, active Page, state, and canonical location together. Any persistence error prevents session creation and is reported with its diagnostic code and source path.

## Deterministic initial state

The fixture's only Page, `notebook`, is the default. Session reconciliation selects its first Card, `physical-atom`. Both Cards use the existing collapsed default `OblivionCardViewState`. The existing independent toggle path preserves explicit `MULTI_EXPAND`: expanding either Card does not select it or collapse its sibling.

## Workspace and session ownership

Workspace truth remains in JSON, TOML, Markdown, Persistence, and Model: identities, titles, order, kinds, content, and provenance. Session truth remains only in `OblivionSessionState`: active selection, independent expansion, page/body scroll offsets, and other transient view state. No session state is written to the vault.

M19i retains the immutable product-owned `OblivionStandaloneStyles.M19h`. No visual measurements, colors, layout rectangles, or host state appear in the vault.

## Reload behavior

Reload is a process restart or a fresh `OpenWorkspace` call. There is no watcher or live reload. A fresh open re-reads all three metadata levels and Markdown bodies, then creates deterministic fresh session state. Tests edit Markdown and Card TOML in temporary vaults and prove the changed content/title appears only after explicit re-open.

## Standalone integration and visual parity

`OblivionStandaloneSurface` no longer calls `M19hTwoCardStack.Materialize`; it requires the real session's active Page and asserts the bounded proof contains exactly two Markdown Cards. The host still uses the same full-screen window, page `ScrollViewer`, Machina VStack, 24px gap, Card shell, 40px square affordance, selected border, responsive widths, and mature Avalonia read-only document presenter. Presenter, inspector, sidebar, tabs, and new content kinds remain absent.

The old M19h two-Card Presentation fixture has been removed from the standalone project. M19h geometry, interaction, scroll, style, screenshot, and mature-presenter contracts remain covered directly against the vault-loaded surface; the older M19g single-Card fixture remains only as its explicit regression test.

## Qualification

The collapsed and both-expanded 2560x1440 M19i captures are byte-for-byte identical to the corresponding M19h captures. The expanded process reports a 1688px page extent, preserving page overflow. Canonical Presenter playback passed 14/14 with zero failures and skips.

All required `JointTaskForce.slnx`, `Copeland.slnx`, `Oblivion.slnx`, `Machina.UI.slnx`, `Machina.UI.Slow.slnx`, and `Aurelian.slnx` test commands passed. The required no-restore Machina build passed with zero warnings/errors, and `git diff --check` passed.
