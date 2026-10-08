# Aurelian game menu template

Reference `src/Integrations/Aurelian.GameMenus/Aurelian.GameMenus.csproj` to add a basic
title, pause, controls, or completion menu. This Aurelian integration owns menu layout,
standard buttons, selection, InputMan bindings, retained Machina hit testing, and native
Vulkan presentation. The game owns actions and transitions, including saves and quitting.

## Define a page

```csharp
var page = new GameMenuPage("title", "MY GAME", "Choose an action.",
[
    new GameMenuEntry("start", "Start game"),
    new GameMenuEntry("continue", "Continue", Disabled: true),
    new GameMenuEntry("quit", "Quit"),
]);
var navigation = new GameMenuNavigation();
var view = new GameMenuView(font);
```

Include `Input.Map(GameMenuBindings.Map, 100, GameMenuBindings.CreateBindings())` in
the game's InputMan profile. Activate the menu context while a menu is open, and the
gameplay context while playing. Each fixed tick, read the adapter's `InputFrame`:

```csharp
GameMenuInput input = GameMenuBindings.Read(adapter.CurrentFrame);
string? action = navigation.Update(page.Entries, input);
if (action is not null)
{
    ActivateGameAction(action);
}
if (input.Back)
{
    ReturnToPreviousScreen();
}
```

Up/Down selects enabled entries, Enter/Space confirms, and Escape reports Back.
The game decides Back's meaning. Freeze game simulation while menus are open.
Reset navigation when changing screens. Beacon Run provides a complete example in
`BeaconApplication`, `BeaconControls`, and `Program`.

For mouse input, pass logical surface coordinates and press/release events to
`view.Pointer(page, navigation.SelectedIndex, point, down)`. Activate the returned
action through the same game-owned dispatcher. Call `view.CancelPointer()` on focus
loss. A release activates only the matching enabled button; a changed page or selection
cancels the pending press. Layout and hit testing use the same prepared Machina tree.

## Present natively

Copy `SpaceMono-Regular.ttf` and `CrimsonText-Regular.ttf` with their licenses to the
game's assets, as Beacon Run does. Create `AurelianNativeUiFont` from that directory.
Compile the existing `AnalyticShape2D.v.ts` and `MsdfText.v.ts` programs with Aurelian's
shader toolchain, then construct `GameMenuNativePresenter` with the Vulkan plant,
color target, compiled programs, and font. Draw the world first, then call:

```csharp
menuPresenter.Render(view, page, navigation.SelectedIndex);
```

The menu loads the completed world color target and overlays analytic shapes and
MSDF glyphs. It retains layout, geometry, and font atlas uploads across unchanged
frames. Dispose the presenter before the target and Vulkan plant. For a menu-only
application, clear/render the background target before presenting the menu.

The default page supports up to four entries on surfaces at least 640 by 600, with
ASCII text and the supplied font sizes. It is a basic menu template, with no scrolling,
localization, save format, or arbitrary UI operation support. Unsupported native
presentation operations fail explicitly.

## Reuse an existing game's skin

`GameMenuTemplate.AddButtons` accepts `GameMenuButtonLayout` and `GameMenuButtonStyle`.
Sunkill's title menu and TinyFarm's title/pause menus use this shared button producer,
while keeping their artwork, labels, action IDs, and game state machines. The default
`Build` page is available immediately for new games such as Beacon Run.

## Qualification

`tests/Integrations/Aurelian.GameMenus.Tests` exercises disabled navigation, actual
resolved button hit regions, stale presses, focus cancellation, and retained layout
invalidation. Beacon Run's `--proof` route additionally checks native menu captures,
InputMan context isolation, paused simulation, mouse activation, and unchanged-frame
pixel/font-cache stability before completing the real 3D game.
