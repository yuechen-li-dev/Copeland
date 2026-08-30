# Oblivion Host Adapter Elimination M18d

## Outcome

M18d is Outcome A. The M18 extraction arc is architecturally closed.

Before M18d, PresenterOblivionHostAdapter.cs combined product catalog loading,
page composition, resolved-layout interaction-map construction, product scroll
clamping, action invocation, and historical manifest writers. Its interaction
companion combined product hit priority with a Presenter-owned scrollbar state
machine and Presenter action strings.

Both Presenter adapter files are deleted. Product composition now lives in
Oblivion.App.OblivionWorkbench; product hit mapping lives in
Oblivion.UI.Interaction; generic pointer helpers and scrollbar drag mechanics
live in Machina.Runtime.Input.

## Remaining adapter audit

| Former member or responsibility | Classification | Semantic owner | Disposition |
| --- | --- | --- | --- |
| page/card catalog methods and workspace loading | OBLIVION_SESSION_POLICY | Oblivion.App | moved to OblivionWorkbench; host supplies only OblivionHostOptions |
| page row/card/inspector composition methods | OBLIVION_PRODUCT_INTERACTION | Oblivion.App/UI | moved to OblivionWorkbench; no Presenter state type remains |
| selection resolution and local card-state projection | OBLIVION_SESSION_POLICY | Oblivion.UI/App | moved with product workbench and typed host state |
| interaction-map construction from resolved geometry | OBLIVION_PRODUCT_INTERACTION | Oblivion.UI/App | product target construction remains Oblivion-owned |
| card/header/body hit records and lookup | OBLIVION_PRODUCT_INTERACTION | Oblivion.UI | moved to OblivionPageInteractionMap |
| nested scroll-region priority and visible-coordinate adjustment | OBLIVION_PRODUCT_INTERACTION | Oblivion.UI | retained as explicit product policy |
| pointer position/type inspection | GENERIC_HOST_INPUT | Machina.Runtime | moved to UiInputEventExtensions |
| wheel, track, thumb-drag, capture, and release lifecycle | GENERIC_MACHINA_RUNTIME | Machina.Runtime | moved to ScrollbarInteraction |
| scrollbar geometry calculation and clamping | GENERIC_REUSABLE_UI | Machina.Standard | neutralized as ScrollRegion |
| card geometry, line fitting, and frame lookup | GENERIC_REUSABLE_UI | Machina.Standard | neutralized as StandardCard, CardLayoutHelper, and CardFrame |
| Presenter surface coordinate normalization | PRESENTER_DEVTOOL_HOSTING | Presenter | remains a generic subtraction of the content viewport origin |
| Avalonia event collection and pointer capture application | AVALONIA_PLATFORM_ADAPTER | Presenter/Avalonia | remains platform fallback; no product type enters Avalonia |
| historical M11-M15 manifest writers | LEGACY_COMPAT | Oblivion.App | retained with the product workbench for artifact compatibility |
| product action string construction/parsing | LEGACY_COMPAT | Oblivion.UI | replaced by the isolated OblivionUiActions typed codec |
| product action interpretation in PresenterNavigationDispatch | OBLIVION_PRODUCT_INTERACTION | Oblivion.App | deleted; Presenter delegates decoded interactions to OblivionInteractionDispatcher |

The old host adapter knew product page/card IDs, selected and expanded state,
scroll target meaning, effect state, and Presenter layout/navigation types. The
replacement host projections know only workspace path, surface shape, shell
shape, and the two product-owned state values. Presenter does not mutate those
values itself.

## Final flow and dependency direction

~~~text
Avalonia input
  -> Machina.Runtime UiInputEvent
  -> Machina.Runtime generic scrollbar mechanics
  -> Oblivion.UI interaction map and typed interaction codec
  -> Oblivion.App interaction dispatcher
  -> OblivionSessionState / OblivionApplicationState
~~~

~~~text
Presenter -> Oblivion.App -> Oblivion.UI -> Machina.UI
Oblivion.* -X-> Presenter
Machina.UI -X-> Oblivion
~~~

Presenter still owns its development palette, window, page chrome, playback
driver, and Aurelian/Avalonia adapters. Those are host mechanics, not Oblivion
semantics.

## Playback impact

Playback target names and TOML remain unchanged. The resolver now consumes
OblivionScrollTarget rather than Presenter product enums. All 14 canonical
scenarios pass with their original assertions.

## What remains

No Presenter-owned product-semantic compatibility seam remains. The only
adapters are generic platform/input and development-host projections. Avalonia
remains an allowed fallback boundary.

