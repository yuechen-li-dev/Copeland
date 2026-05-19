# Machina Dominatus Runtime (M0a)

## Goal

M0a introduces a tiny Dominatus-backed runtime for the counter sample.

This milestone proves the action loop:

1. Presenter receives a pointer click.
2. Presenter hit-tests and resolves a `UiAction`.
3. Presenter sends action to Dominatus runtime as a typed `UiActionEvent`.
4. Runtime node consumes event and updates blackboard state.
5. Presenter rebuilds UI from runtime state and redraws.

## Scope

Included in M0a:

- Counter runtime helper in `Machina.Dominatus.Runtime`.
- `UiActionEvent` typed action ingress event.
- Blackboard-backed `counter.count` state.
- Event handler that increments count for `increment` action.
- UI declaration generated from runtime blackboard state.

Not included in M0a:

- General app runtime framework.
- Navigation/screen stacks.
- Focus or keyboard/text input.
- Routing or event bubbling.
- Modal/dialog systems.

## Runtime model

`CounterUiRuntime` owns:

- Dominatus world and agent lifecycle for this sample scenario.
- Blackboard key and count state.
- Action dispatch as typed events.
- UI declaration generation from blackboard values.

Presenter remains intentionally small and dumb:

- pointer capture
- hit-test
- send action
- tick runtime
- redraw

## Default vs advanced usage note

The Dominatus counter runtime proof remains valuable for demonstrating event ingress, blackboard-backed state, and runtime orchestration.

For the default presenter sample counter interaction, M0d now uses `Machina.Runtime.Dispatch` because the transition is a simple deterministic field update and does not require orchestration machinery.
