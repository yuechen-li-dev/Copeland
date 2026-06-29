# Machina Presenter Card Hardening M11e

## Purpose

M11e is a presenter/Oblivion stabilization milestone.

It fixes the manual-review card layout bugs from M11d, hardens future card authoring around a shared layout helper, keeps the M10 presenter shell behavior intact, and keeps M11d JSON/TOML persistence intact.

## Bugs fixed

- presenter text cards no longer compute body height in the wrong coordinate frame
- presenter bullet lines no longer lose leading content because the bullet prefix now reserves width separately from content clipping
- the legacy hosted card no longer paints a full-width dark body background behind partially sized hosted content
- scrollbar track/thumb geometry is now clamped fully inside the visible shell chrome on overflowing pages

## Card coordinate frames

M11e makes the coordinate frames explicit:

- outer card rect is the full card bounds
- content rect is the inset card interior in outer-card coordinates
- header/body/footer regions are content-local
- `bodyTop` is content-local and must never be mixed with outer-card height math

The shared helper computes these regions once and card builders consume the named regions instead of repeating width/height arithmetic inline.

## Shared card layout helper

`PresenterCardLayoutHelper` now centralizes:

- outer/content/header/body/footer region computation
- inner width and inner height derivation
- body line-capacity calculation
- deterministic line clipping and ellipsis behavior

`PresenterCard` and `OblivionCardRenderer` now both use that helper.

Future card authors should derive layout from the helper first and then place title, badges, body text, and hosted content into the named regions.

## Body text line capacity

The original presenter bug came from subtracting the card inset twice when computing body height.

M11e fixes that by computing inner height once and deriving body height from:

```text
bodyHeight = innerHeight - bodyTop
```

The same shared region model now drives Oblivion cards so persisted cards follow the hardened path too.

## Bullet clipping policy

Presenter bullet lines keep the bullet marker.

Clipping now works like this:

1. reserve the bullet prefix width
2. clip the content text against the remaining width
3. append ellipsis only to the content portion when needed

This avoids the old failure mode where the bullet consumed too much of an already-too-tight width and made the first visible characters look cut off.

## Hosted card background policy

Hosted-card body wrappers must stay bounded and must not paint accidental full-width fills behind smaller hosted content.

M11e adopts the safe default:

- hosted wrapper border is allowed
- hosted wrapper background is transparent unless there is a deliberate design reason to fill it

This removes the dark rectangle bleed on `Legacy -> M1e Card`.

## Scrollbar thumb geometry fix

The presenter shell keeps the M11c cached-composition model.

M11e hardens geometry by:

- insetting the track slightly inside the viewport chrome
- clamping thumb top and bottom inside the track rect
- preserving deterministic hide/show behavior for short pages

Overflowing pages now keep the thumb visible at top, middle, and bottom positions without drawing past the track.

## Authoring rules for future cards

- use `PresenterCardLayoutHelper` instead of recomputing inset/body geometry inside a card builder
- treat outer-card and content-local coordinates as separate frames
- compute body text capacity from the shared layout regions
- use the shared clipping helpers for deterministic truncation
- keep hosted-card backgrounds transparent unless the fill is intentionally part of the card design
- keep this sample-local until a real production component contract is justified

## Export commands

```powershell
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11e\presenter-card-hardening-oblivion-cards.png -SelectedSection oblivion -SelectedTab cards
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11e\presenter-card-hardening-oblivion-roadmap.png -SelectedSection oblivion -SelectedTab execution-roadmap
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11e\presenter-card-hardening-components-controls.png -SelectedSection components -SelectedTab controls
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11e\presenter-card-hardening-components-controls-bottom-scroll.png -SelectedSection components -SelectedTab controls -ScrollPage components.controls:9999
.\tools\Export-MachinaPresenter.ps1 -OutputPath artifacts\m11e\presenter-card-hardening-legacy-m1e-card.png -SelectedSection legacy -SelectedTab m1e-card
```

## What changed

- shared presenter/Oblivion card layout helper added
- presenter text-card body geometry fixed
- bullet clipping hardened
- Oblivion persisted cards moved onto the shared layout path
- legacy hosted-card background bleed removed
- scrollbar track/thumb geometry clamped more safely
- regression tests added for card math, clipping, hosted-card fill, scrollbar geometry, and exports

## What did not change

- no Roslyn execution
- no xUnit `[Fact]` / `[Theory]` notebook/runtime execution
- no markdown editor
- no Visionary editor/runtime
- no new notebook/editor/runtime features
- no font or MSDF work resumed
- no production renderer/core/layout semantic change
- JSON/TOML persistence behavior remains the M11d model

## Deferred work

- executable Oblivion cards
- notebook/runtime `[Fact]` / `[Theory]` execution
- markdown authoring/editing
- Visionary source workspace/editor implementation
- any promotion of these sample card helpers into a broader production component library without a separate milestone
