# Screen state and automatic preparation invalidation

## Dominatus evaluation

Dominatus fits TinyFarm's coarse screen and modal workflow. `HfsmInstance` already
implements push, pop and goto; `NodeRunner.Exit` cancels the node's context and
disposes its enumerator. A pushed parent remains on the stack. These semantics
match playing → pause → inventory → back without separate return-to-pause flags.

The probe in `TinyFarmUiFlowTests` verifies that real Dominatus transitions cancel
child scopes, preserve parent values, handle entry followed by immediate exit,
reset the same screen with a fresh scope, and cancel every scope on shutdown.
Entry registers cancellation in the node factory, before returning an iterator:
an iterator's body/finally alone cannot manage a state that exits before its first tick.

The chosen adapter is [TinyFarmUiFlow](../../../Games/TinyFarm/TinyFarm.InputMan/TinyFarmUiFlow.cs),
using TinyFarm's existing sibling-source `Dominatus.Core` dependency. The older,
package-backed `Machina.Dominatus.CounterUiRuntime` remains a separate smoke proof;
this change does not switch its package authority or add Dominatus to Machina.
The UI world is separate from the simulation's agent world and never checkpointed.
Only navigation requests tick it; widgets do not get agents, coroutines, or worlds.

## State ownership

[UiStateScope](../../../src/Machina.UI/Machina.Runtime/State/UiStateScope.cs) is a
Machina-owned transient store keyed by `NodeId`. Reusing a slot with a different
type or lifetime is an error. State values are ordinary C# values; the host owns
scope entry, suspension and disposal. The lifetime token lets scoped work stop on exit.
Disposal drops values, rather than invoking arbitrary `IDisposable` widget values;
resource-owning work should register cleanup with the lifetime token.

| Lifetime | TinyFarm examples | On suspension | On exit |
| --- | --- | --- | --- |
| Scope | Search text, filters, sort, selection, scroll offsets, pause selection, craft inputs, container paging | Retained | Discarded |
| Activation | Search focus, pointer press, scrollbar drag | Discarded | Discarded |

`TinyFarmMenus` remains the game policy facade for filtering, sorting and row
selection. Its data lives in the current scope. `TinyFarmWidgetStates` owns the
game-specific shapes; the generic store owns lifetime. Craft and container staging
also use their screen's scope. Returning from a child resumes its parent scope;
closing and reopening inventory or stats creates a fresh entry. These transient
values do not persist across loads, title returns or app disposal.

Pointer activation checks both node identity and action, then consumes the press.
A screen switch followed by re-entry cannot complete an old press, even if no
presentation was requested between transitions. Suspending a parent also drops
its captures, so popping a modal cannot revive its held pointer press.

## Automatic cache invalidation

[MachinaPresentationCache](../../../src/Machina.UI/Machina.Pipeline/MachinaPresentationCache.cs)
accepts the actual pure declaration. `UiDeclarationSnapshot` records its scalar
record values and ordered collection contents. Child references are replaced by a
shared sentinel before record equality, then traversed separately. This preserves
all record fields without a second hand-maintained list of invalidation fields.
It also notices in-place edits to the author's child/track lists.

An equal declaration, viewport and lowering configuration reuse the prepared
layout, presentation and hit-test index. A changed entry is published only after
preparation succeeds. The comparison uses exact values, not hash equality.
Unknown nodes/payloads conservatively prepare again. Standard rich-text metadata
uses value equality, including its font measurer identity. Font services must keep
their metrics stable, or the host must invalidate when the service changes;
`TinyFarmNativeUi.SetTextMeasurer` does this explicitly.

TinyFarm keeps base, clock, prompt and inspector caches in the active screen scope. Its
native `TinyFarmUiKey` presentation record, menu/craft/container cache strings, inventory hash and journal
progress key are removed. A field that changes the declaration therefore changes
the cached presentation automatically. Regression tests cover spirit, money and
item-name changes that were missing from the previous key, independent clock
preparation, and inspector fact changes without a viewport change.

## Cost and boundary

This compares complete declarations and rebuilds complete preparation after a
change. It does not provide subtree reconciliation, widget mounting, dependency
tracking for arbitrary mutable services, or zero-allocation declaration building.
Opaque rich-text payloads trade reuse for correctness.

A local warmed .NET 10 Debug sample with TinyFarm's real MSDF metrics measured
about 78 microseconds and 62 KB per unchanged HUD declaration, with one preparation
across repeated requests. This is an observational sample, not a portable timing
guarantee. Profiling first found unnecessary temporary glyph-run construction in
font measurement; measurement now sums the same unkerned atlas advances that this
host realizes, verified against actual glyph runs by the native journal test.

The normal validation lanes are `dotnet build Machina.UI.slnx`,
`dotnet test Machina.UI.slnx`, the full `TinyFarm.Core.Tests` project, and TinyFarm's
native `shell-native.json`, `characters-native.json`, and `ui-lifetimes-native.json` playtests. Focused tests
also cover state disposal, activation capture, cache equality and failed preparation.
