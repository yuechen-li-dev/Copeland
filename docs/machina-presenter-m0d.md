# Machina Presenter M0d

## What M0d changes

M0d switches the default live counter sample from ad hoc mutation / Dominatus runtime state to a local dispatch-table state model.

The presenter now owns:

- `CounterState` record (`Count` field)
- static `DispatchTable<CounterState>` with one `Increment` transition for `increment`
- action application through dispatch
- redraw only when dispatch returns a changed state reference

## Why dispatch table is the right default here

The counter interaction is a single deterministic field transition:

- event: `increment`
- state change: `Count = Count + 1`

For this shape, dispatch tables are the best default:

- explicit typed transition declaration
- immutable state updates via `with`
- no runtime orchestration overhead
- no-op identity behavior supports efficient redraw gating

## Runtime behavior

On startup:

- title includes `count: 0`
- rendered text shows `Count: 0`

On click:

- clicking `Increment` resolves `UiAction.Named("increment")`
- presenter dispatches through `CounterDispatch`
- when dispatch returns a new state reference, presenter updates `_state` and redraws
- title/log still show the action name and new count

No-op behavior:

- unknown action names return original state, no redraw
- non-action clicks do not dispatch and do not redraw

## Dominatus runtime proof remains

The Dominatus-backed counter runtime (`CounterUiRuntime`) and associated tests remain in place as an advanced proof of runtime control and orchestration.

It is intentionally **not** the default for this simple presenter counter path.

## Run

```bash
dotnet run --project samples/Machina.Presenter.Sample
```

In headless environments, validate via build/test commands and run the UI manually in a desktop-capable environment.
