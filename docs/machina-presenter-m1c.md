# Machina Presenter Sample M1c: Settings + Counter Dispatch-Table Demo

## Scope

M1c expands `samples/Machina.Presenter.Sample` from a counter-only interaction into a small settings panel that demonstrates multiple standard controls and immutable state transitions.

The sample now demonstrates:

- Increment button (`counter.increment`)
- Checkbox toggle (`settings.emailUpdates.toggle`)
- Switch toggle (`settings.notifications.toggle`)

All simple state transitions route through `DispatchTable<DemoState>` created with a collection expression.

## State shape

The presenter uses a sample-local immutable record:

```csharp
private sealed record DemoState(
    int Count,
    bool EmailUpdates,
    bool Notifications);
```

Initial state:

- `Count = 0`
- `EmailUpdates = true`
- `Notifications = false`

## Dispatch model

The sample defines action IDs once in a static `Actions` class and reuses them in both view metadata and dispatch transitions.

The sample-local dispatch table is the single transition path:

- `Actions.Increment` (`counter.increment`) -> increment `Count`
- `Actions.ToggleEmailUpdates` (`settings.emailUpdates.toggle`) -> toggle `EmailUpdates`
- `Actions.ToggleNotifications` (`settings.notifications.toggle`) -> toggle `Notifications`

Unknown event names no-op and preserve reference identity.

## Click -> action -> redraw flow

Pointer interactions follow this sequence:

1. Pointer press in Avalonia image surface.
2. Convert to runtime `PointerPoint`.
3. Hit-test against current `UiHitTestIndex`.
4. Resolve action name, or `<none>`.
5. Dispatch action through `DispatchTable`.
6. Redraw only when dispatch returns a different state reference.
7. Update window title/status text.

This keeps redraws gated by meaningful state changes.

## Dominatus note

Dominatus is not used for these simple transitions.

M1c intentionally keeps this path as a direct hit-test + `DispatchTable` sample to prove the lightweight default for deterministic, local state updates.

## Expected manual behavior

- Clicking **Increment** increases count and updates text.
- Clicking **Email updates** toggles checkbox state and label text (`on`/`off`).
- Clicking **Notifications** toggles switch state and label text (`on`/`off`).
- Clicking non-action areas reports `<none>` and does not redraw state.

## Run

```bash
dotnet run --project samples/Machina.Presenter.Sample
```

## M3e follow-up

M3e keeps the same dispatch-table state model but composes checkbox/switch controls from explicit flat rows (`email-row` and `notifications-row` sub-parts) to improve inspectability and hit-test targeting.
