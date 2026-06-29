# Machina Presenter Navigation Shell M10a

## Purpose

M10a adds a presenter organization shell so the sample can keep growing without collapsing into one long mixed proof screen.

## Why presenter navigation is needed now

The M9 font phase is closed enough for current needs:

- `DirectOutlineStatic` remains the static/reference text path.
- MSDF remains explicit experimental/scalable after the structural repair work.
- production UI text defaults remain unchanged.

That leaves the presenter sample itself as the next pressure point. More sample content now needs app-level organization rather than more ad hoc vertical stacking.

## Navigation hierarchy

```text
PresenterApp
  SidebarSection[]
    LocalTab[]
      Screen/Page content
```

Sidebar is app-level section navigation.
Tabs are local to the selected sidebar section.
Pages are the selected section/tab content surface.

## Sidebar sections

M10a uses a small localized section model:

- `Overview`
- `Components`
- `Text`
- `Diagnostics`

The sidebar is a fixed-width visible rail and does not scroll with the page content.

## Local tabs

Tabs are rendered only for the selected sidebar section.

Current local tab groupings include:

- `Overview`: `Home`, `Status`
- `Components`: `Controls`, `Cards`
- `Text`: `Bitmap/current`, `DirectOutlineStatic`, `MSDF experimental`
- `Diagnostics`: `Layout`, `Export`

Changing section restores the last selected local tab for that section when available.

## Page/screen model

Each section/tab resolves to one stable page id such as:

- `overview.home`
- `components.controls`
- `text.direct-outline-static`

The first/default page keeps the original presenter settings content path alive inside the new shell rather than replacing it with unrelated content.

## Scrollable content region

M10a adds a presenter-local vertical scroll region:

- fixed viewport rectangle
- explicit content height
- explicit scroll offset per page id
- clamped offset in the range `0..max(0, contentHeight - viewportHeight)`

Wheel input is still deferred. M10a focuses on structure, state, and deterministic exportability.

## Scrollbar geometry

Scrollbar visuals are deterministic and proof-level for now.

- the track lives beside the content viewport
- the thumb is visible only when content overflows
- thumb height derives from the viewport/content ratio
- thumb position derives from `scrollOffset / maxScrollOffset`

The current shell uses a hidden scrollbar when content fits.

## State and actions

Navigation state is an immutable record:

```csharp
public sealed record PresenterNavigationState(
    string SelectedSectionId,
    IReadOnlyDictionary<string, string> SelectedTabBySectionId,
    IReadOnlyDictionary<string, double> ScrollOffsetByPageId);
```

Actions stay explicit through `UiActionId` values with stable prefixes:

- `presenter.navigation.select-section|...`
- `presenter.navigation.select-tab|...`
- `presenter.navigation.set-scroll-offset|...`

Components do not own global navigation state.

## Dominatus lifecycle/state decision

Dominatus was not used directly for presenter navigation lifecycle in M10a.

Why:

- M10a introduces no async effects, modal scopes, or mount/unmount orchestration that would materially benefit from `Machina.Dominatus.Runtime`.
- the new navigation behavior is a pure explicit record + reducer problem.
- keeping navigation state as plain immutable sample state avoids needless coupling.

Where Dominatus still remains relevant:

- the existing Machina render pipeline still dispatches render commands through the Dominatus-backed raster actuation path.

So M10a reuses the existing render infrastructure indirectly, but does not add a new Dominatus lifecycle layer for navigation itself.

## Export commands

Build and test:

```powershell
dotnet test Copeland.slnx
dotnet build Copeland.slnx --no-restore
```

Representative shell export:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10a\presenter-navigation-shell-overview.png -IncludeNavigationShell
```

Representative scrolled export:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10a\presenter-navigation-shell-scrolled.png -IncludeNavigationShell -NavigationPage components.controls -ScrollPage components.controls:120
```

Text-section export:

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m10a\presenter-navigation-shell-text.png -IncludeNavigationShell -NavigationPage text.direct-outline-static -IncludeDirectOutlineRenderBridgeProof
```

## What changed

- presenter sample now supports an opt-in navigation shell
- sidebar sections and local tabs now organize sample content
- page selection state and per-page scroll state are explicit
- vertical scrollbars now have deterministic sample-local geometry
- shell exports now write a navigation manifest next to the PNG

## What did not change

- no production UI renderer default changed
- no `Machina.Core` document-model semantic changed
- no `Machina.Layout` resolver behavior changed
- no `Standard.Text` semantic changed
- no new font work was started in M10a
- no MSDF or direct-outline behavior changed
- no generic router framework was introduced

## Deferred work

- wheel/keyboard scroll input
- broader clipping semantics in shared layout/renderer layers
- focus/accessibility systems
- reusable Standard sidebar/tab widgets
- any production text integration follow-up from M9 unless a concrete need appears
