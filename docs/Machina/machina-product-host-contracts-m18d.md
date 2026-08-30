# Machina Product Host Contracts M18d

## Law

~~~text
Machina reports interactions.
Products interpret semantics.
Hosts execute explicit capabilities.
~~~

## Machina ownership

Machina.Runtime owns platform-neutral UiInputEvent records, pointer-coordinate
inspection, pointer button/wheel classification, and the generic scrollbar
state machine. ScrollbarInteraction knows a generic target token, resolved
track/thumb geometry, viewport height, wheel multiplier, and pointer lifecycle.
It returns only a requested numeric offset, consumed state, and pointer-capture
request.

Machina.Standard owns reusable card and scrollbar realization: StandardCard,
CardFrame, CardLayout, CardTextLayout, CardLayoutHelper, ScrollRegion, and
ScrollbarGeometry.

Machina does not know Oblivion page, card, inspector, raw-source, expansion,
selection, or effect meaning.

## Product ownership

Oblivion.UI assigns semantic targets to resolved regions, chooses nested scroll
priority, maps generic offsets to typed product interactions, and owns the
Machina-action compatibility codec.

Oblivion.App validates those interactions, reduces session state, validates
product action declarations, creates typed effect requests, and applies typed
results.

## Host ownership

A host collects native input, normalizes it into Machina events, supplies a
surface/shell projection, stores the returned product state, renders output,
and optionally supplies explicit OblivionHostCapabilities.

Presenter owns only its palette navigation, page chrome, playback, exports,
window lifecycle, pointer-capture application, and Avalonia/Aurelian
implementation details. Another host can use the same product workbench,
interaction map, dispatcher, and capability contracts without copying Presenter
behavior.

## Explicit exclusions

This boundary is not a generic event framework. It adds no service locator,
command bus, provider layer, lifecycle framework, control mirroring, authoring
redesign, or Avalonia replacement.

