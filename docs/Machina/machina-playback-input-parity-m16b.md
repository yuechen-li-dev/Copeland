# Machina Playback Input Parity M16b

## Purpose

M16b stabilizes Machina presenter playback input parity for the two M16a wheel blockers:

- `main-stack`
- `raw-source`

Playback remains internal presenter input routing only. It does not add native OS automation, pixel-golden diffing, TOML scripting, or product UX features.

Core doctrine:

```text
Playback steps must exercise the same presenter input/routing path that a real user would exercise.

Initial scenario state may be set directly.

Interaction steps must not mutate final presenter state directly.

If playback and direct tests disagree, find the seam mismatch.

Do not patch playback by bypassing hit testing, dispatch, or scroll routing.
```

Preserved M16a doctrine:

```text
TOML playback is a cassette tape, not a scripting language.

TOML describes a linear stack of input steps and assertions.

If conditionals, loops, generated cases, or complex logic are needed, write C# tests/helpers that call the playback runner.
```

## M16a blockers

M16a shipped as a useful MVP but left two parity blockers open:

- playback wheel routing for `main-stack` did not preserve the dedicated M15f scroll state after dispatch
- playback wheel routing for `raw-source` did not match the real visible-coordinate path that a user drives through the presenter shell

## Root-cause method

Each failing path was traced by comparing:

1. scenario and wheel step
2. requested semantic target
3. resolver output
4. emitted wheel input
5. hit-test result
6. dispatched action
7. expected scroll-state field
8. actual scroll-state field
9. the equivalent direct M15f seam

The trace now records deterministic target-resolution, hit-test, dispatched-action, and scroll-delta evidence so failures stay reviewable.

## Main-stack wheel parity

Scenario: `samples/Machina.Presenter.Sample/PlaybackScenarios/oblivion-main-stack-scroll.machina-playback.toml`

Expected state change:

- `main-stack` scroll offset increases
- inspector scroll offset remains unchanged

Actual M16a state change:

- playback emitted `presenter.navigation.set-oblivion-main-card-stack-scroll|oblivion.docs|48`
- the next render showed `main-stack` scroll offset back at `0`

## Main-stack root cause

The playback wheel path already dispatched the dedicated M15f `SetOblivionMainCardStackScrollOffset` action.

The divergence happened after dispatch:

- wide Oblivion stores `main-stack` scroll in the page scroll field
- `PresenterNavigationRenderSession` always wrote the shell page scrollbar geometry back into that same field
- wide Oblivion shell page scroll has `MaxScrollOffset = 0`
- the shell write-back therefore clamped the field back to `0`

This was not a wheel-routing miss. It was a render-session overwrite bug.

## Main-stack fix

M16b stops the shell page-scrollbar write-back from overwriting wide Oblivion `main-stack` state.

Result:

- playback wheel over `main-stack` now keeps the dedicated M15f action result
- `oblivion-main-stack-scroll` passes
- trace shows target resolution, dispatched action, hit-test region, and state delta

## Raw-source wheel parity

Scenario: `samples/Machina.Presenter.Sample/PlaybackScenarios/oblivion-raw-source-scroll.machina-playback.toml`

Expected state change:

- `raw-source` scroll offset increases
- `main-stack` remains unchanged

Actual M16a state change:

- semantic target existed
- resolved playback point was offscreen or fell back to inspector-pane routing
- wheel updated inspector scroll or no local raw-source offset at all

## Raw-source root cause

Two exact mismatches were present:

1. The starter scenario did not reveal the raw-source viewport clearly enough for visible-coordinate playback, so M16b now sets deterministic initial inspector scroll in the scenario.
2. The nested raw-source route accepted the older interaction-map coordinate seam, but the real shell-visible playback path reached `OblivionInteraction` with visible coordinates. The nested raw-source hit check therefore missed and fell back to `InspectorPane`.

In concrete terms:

- playback resolved `raw-source` from the real presenter shell
- the visible wheel point exercised root-visible input routing
- nested raw-source hit testing still preferred the older pre-scroll interaction-map coordinate seam
- wheel therefore dispatched `SetOblivionInspectorScrollOffset` instead of `SetOblivionRawMarkdownSourceScrollOffset`

## Raw-source fix

M16b keeps the old direct seam working and adds the real visible-coordinate path:

- the starter scenario now initializes `inspectorScroll = 240`
- playback resolver translates raw-source target geometry through the inspector-scroll transform before choosing a visible point
- nested scroll-region hit testing now recognizes both the older direct seam and the real shell-visible seam for nested raw-source interaction

Result:

- playback wheel over `raw-source` dispatches `SetOblivionRawMarkdownSourceScrollOffset`
- `main-stack` remains unchanged
- `oblivion-raw-source-scroll` passes

## Trace improvements

M16b trace now includes deterministic fields for:

- target resolution
  - semantic target kind
  - requested card id
  - resolved card id
  - resolved region kind
  - resolved region id
  - resolved point
  - resolved rect
- hit-test result
  - region kind
  - region id
  - card id
  - scroll region id
  - local point
- dispatched action
  - action id
  - action type
  - whether an action was handled
  - whether wheel input was consumed
- state deltas
  - main-stack
  - inspector
  - raw-source
  - expanded-body

## Scenario format boundary

M16b preserves linear TOML data only.

The parser now rejects programming-like fields such as:

- `if`
- `then`
- `else`
- `loop`
- `while`
- `until`
- `for`
- `repeat`
- `script`
- `eval`
- `expr`
- `condition`
- `callback`

Mandatory assertion reasons remain enforced.

## Preserved non-goals

M16b does not implement:

- native OS automation
- Selenium, Playwright, Appium, or `SendInput`
- pixel-golden screenshot diffing
- Markdown editing
- notebook execution
- Roslyn execution
- Aurelian work
- `VD-MIR` work
- new product UX features

## What changed

- wide Oblivion render-session scroll state no longer overwrites dedicated `main-stack` playback results
- nested raw-source playback now routes through the real visible shell path
- starter raw-source scenario now sets the inspector state needed to expose the target
- playback trace carries explicit parity evidence
- parser now rejects scripting-like TOML keys
- parity regression tests now cover main-stack, raw-source, trace fields, starter scenarios, and scenario boundaries

## What did not change

- playback still uses internal presenter input routing
- interaction steps still do not mutate final state directly
- TOML is still linear data only
- no native automation was added
- no pixel-golden diffing was added

## Deferred work

- broader semantic target coverage beyond the current starter set
- broader drag authoring beyond scrollbar-focused paths
- possible future extraction of playback tooling from the sample after seams remain stable
