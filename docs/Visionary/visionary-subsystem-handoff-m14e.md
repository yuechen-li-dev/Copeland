# Visionary Subsystem Handoff M14e

## Purpose

M14e records the current subsystem boundaries and the intended reviewer ownership model after the Aurelian migration arc closeout.

## Why the Aurelian arc is closing for now

The Aurelian lane is closing for now because it has reached:

- monorepo placement that is stable enough to live with
- a separate solution lane that restores, builds, and tests
- selected docs dogfood through Oblivion Docs
- a visible-triangle sample that routes through `PresenterScreenStack` as a semantic world screen

That is enough golden-path surface to hand off future rendering/compiler continuation to specialized reviewer lanes without keeping the main repo focus on Aurelian every milestone.

M14e is closeout/handoff only. It does not mark Aurelian as permanently finished.

## Current subsystem boundaries

The current subsystem boundaries are documentation and contract boundaries first. Shared contracts and milestone docs are the cross-reviewer coordination mechanism.

## Copeland

`Copeland` is the compiler workshop:

- frontend/HIR/MIR/backend infrastructure
- compiler doctrine and lane taxonomy
- future SDSL-V migration work if it is ever earned
- future `VD-MIR` extraction only if a later reviewer explicitly resumes that lane

## Machina

`Machina` is the UI/document/presenter/workbench shell:

- presenter/runtime shell
- `PresenterScreenStack`
- card rendering
- layout/input/export paths
- workbench-oriented samples

## Oblivion

`Oblivion` is the notebook/card/document workspace layer hosted inside Machina:

- Markdown/docs dogfood
- card/workspace/inspector UX
- action/effect shell integration
- future editing and execution surfaces

## Aurelian

`Aurelian` is the rendering infrastructure lane:

- render contracts
- runtime/render topology
- shader/compiler lane as currently hosted
- Vulkan-oriented path
- visible proof samples

## Dominatus

`Dominatus` is the orchestration/lifecycle/effect-routing/control-plane lane:

- lifecycle and effect routing patterns
- actuator/control-plane infrastructure
- explicit orchestration seams shared across subsystems

## Leviathan

`Leviathan` remains the future web/auth/payment/social/networked application lane.

It is not part of the active M14e implementation scope.

## Reviewer ownership model

Intended reviewer lanes:

```text
Aurelian reviewer:
  rendering infrastructure
  visible triangle
  runtime/render topology
  Vulkan/native path
  render contracts
  Aurelian samples

Copeland / VD-MIR reviewer:
  compiler workshop
  SDSL-V audit follow-up
  VD-MIR M0/M1/M2/M3
  HLSL/DXC backend
  Slang/PTX backend planning

Machina reviewer:
  UI/document/presenter/workbench shell
  PresenterScreenStack
  card rendering
  layout/input/export

Oblivion reviewer:
  notebook/card/workspace layer
  Markdown/doc dogfood
  inspector/action/effect UX
  future editing/execution surfaces

Dominatus reviewer:
  orchestration/effect routing/lifecycle/control-plane patterns

Leviathan reviewer:
  web/auth/payment/social/networked app layer
```

## Primary focus after M14e

Primary active focus should return to Machina and Oblivion.

The repo has enough Aurelian golden path for now, and the most valuable next main-lane progress is in the user-facing workbench shell rather than deeper compiler or rendering detours.

## Aurelian follow-up lane

Future Aurelian follow-up should be a separate reviewer lane focused on:

- renderer/runtime evolution
- sample/runtime proof maintenance
- Vulkan/native path hardening
- future render-contract work

It should not be implicitly reopened by ordinary Machina or Oblivion milestones.

## Machina / Oblivion follow-up lane

Recommended next main-lane direction:

```text
M15a:
  Machina/Oblivion workbench usability re-entry audit

M15b:
  Presenter resizing and readable card previews
```

Suggested goals:

- inspect presenter/workbench UX after the Aurelian detour
- stabilize `PresenterScreenStack` doctrine across samples
- revisit Oblivion cards/docs/inspector ergonomics
- choose the next user-facing workbench milestone
- avoid new compiler/rendering work in the main lane

M15a now lands as audit/planning only:

- speed remains a documented strength
- readability and resizing are the current blocker
- the recommended implementation follow-up is M15b, not a broad shell rewrite

## Non-goals

M14e does not:

- implement `VD-MIR`
- create Copeland `VD-MIR` packages
- move SDSL-V into Copeland
- add PTX or Slang backends
- add a general Machina/Aurelian bridge
- change renderer behavior
- perform Machina/Oblivion feature work
