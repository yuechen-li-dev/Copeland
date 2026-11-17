# Native pointer feedback and window chrome

Machina now carries native pointer intent, immediate hover/pressed surfaces and
an optional client-drawn Windows title bar. The actual editor and component
gallery use this path; these are not browser cursor overlays or UI mocks.

## Shared ownership

| Layer | Responsibility |
|---|---|
| `Machina.Core.Semantics.UiCursor` / `UiCursorPolicy` | Backend-neutral cursor intent and role defaults; optional `UiSemantics.Cursor` override |
| `Machina.Runtime.Input.UiPointerFeedback` | Hover/press identity for ordinary declared actions, refreshed against current clipped hit geometry; never invokes commands |
| `Machina.Standard.Widgets.WidgetSession` | Existing widget interaction authority; exposes `Cursor` / `IsPressed`, preserves I-beam during text selection and slider cursor during capture |
| `Machina.Standard.Theme.StandardInteraction` | Theme-relative immediate surface colors for hover/press, preserving selected state as the base |
| `Machina.Pipeline.MachinaPointerPresentation` | Rebuilds presentation using existing layout and adjusted colors; no lowering, layout, font measurement or application mutation |
| `Aurelian.Machina.AurelianCursor` | Non-owning native Silk/OS cursor adapter; assigns standard shape only on transitions; unsupported shapes fall back to arrow |
| `Machina.Standard.Windowing.WindowTitleBar` | GPU-ready title, geometric icons and hover/pressed colors sharing `WindowTitleBarLayout` with native hit testing |
| `Aurelian.Machina.AurelianWindowChrome` | Optional Win32 subclass for client caption, native drag/resize/system commands and caption capture; no graphics in callbacks |

No new package or rendering framework was added. Existing Machina targets remain
.NET 10 and Valencia remains .NET 11. Existing `StandardUI` consumers can opt into
pointer feedback without migrating their document or every control to widgets.

## Cursor rules

- Clickable buttons, checkboxes, switches, radio options, tabs and menu items use
  the hand. Disabled widgets use the ordinary arrow and do not highlight.
- Editable/selectable text fields use the **I-beam**. This remains in effect while
  dragging a text selection beyond the field. Static, nonselectable labels stay
  arrows; rendering text does not imply a selection implementation.
- Sliders use horizontal/vertical resize intent. Captured gestures retain their
  cursor until release/cancellation.
- Application canvases can use move, resize, crosshair, not-allowed and wait intents.
  Valencia maps clip body/edge, razor, locked clips and ruler to the appropriate
  intent using the same timeline hit test as editing.
- Refresh pointer state after changing geometry/overlays. The widget pipeline
  rechecks a stationary pointer and bounds any extra preparation pass. Closed or
  covered widgets must not keep stale hover.
- Focus loss clears hover/armed presses. Release outside the pressed control
  removes pressed feedback without activating it. Cursor changes are transient;
  they never enter a project snapshot, edit plan or undo history.

## Native host integration

For widgets, use one `WidgetSession` and one native input adapter per window:

```csharp
// After input.Handle and after preparation that can change routing:
nativeInput.SetCursor(session.Cursor);
```

`AurelianWidgetInput.Poll` also samples native position changes. Win32 non-client
movement does not send GLFW client mouse motion, so relying only on callbacks
would leave a previous button highlighted when entering the title bar. This
sampling uses the existing mouse device and produces the same bounded input
events; it does not create another pointer authority or service.

For legacy `StandardUI` actions, `UiPointerFeedback.Refresh(prepared.HitTest)`
updates the current geometry, and Move/Press/Release update visual state. Call
`MachinaPointerPresentation.Apply` using the **original prepared presentation**
each time, then submit the returned frame to the existing renderer. Do not feed
the tinted result back as the next base, or colors will accumulate. Repaint when
target/press changes; pointer motion inside an unchanged button needs no relayout.

The native window thread owns cursor assignment, input routing, preparation and
presentation. Dispose the cursor adapter before its non-owned input context.
Native callbacks queue/invalidate input; never perform font work, media decoding,
GPU work or layout from a native callback.

## Optional Windows title bar

Own one `AurelianWindowChrome` per native window and dispose it before destroying
the window. It retains the normal resizable window style and removes caption
space through `WM_NCCALCSIZE`. `WindowTitleBarLayout` supplies the same client
rectangles to drawing and native hit testing. Reserve its height in application
layout; do not paint application controls into a draggable caption region.

The current implementation reserves the full blank caption for native dragging
and uses geometric minimize/maximize/close glyphs. Borders/corners return native
resize hits; maximize returns `HTMAXBUTTON`, following Microsoft's
[Snap-layout integration contract](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-snap-layout-menu).
The [custom-frame](https://learn.microsoft.com/en-us/windows/win32/dwm/customframe)
and [WM_NCCALCSIZE](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-nccalcsize)
contracts describe the OS boundary. Signed screen coordinates are converted to
client coordinates; maximized client bounds use the monitor work area.

Caption buttons capture press, cancel on drag-off/focus loss, and post ordinary
system commands. Close reaches the normal host `Closing` handler, so Valencia's
dirty-project confirmation and cancellation remain authoritative. The callback
keeps a rooted subclass delegate and records recoverable managed callback errors;
it never draws or synchronously enters a custom button tracking/render loop.
Keyboard/system-menu commands remain OS-owned. This implementation does not yet
support arbitrary interactive widgets inside the draggable caption.

`chrome.Update()` samples caption hover on the event loop. Observe
`VisualRevision` to invalidate its visuals, and redraw `WindowTitleBar.Create`
with current maximized/hovered/pressed state. The adapter is Windows-only; the
shared cursor/style/geometry contracts remain backend-neutral.

## Validation

Pointer/chrome checks pass, but overall playback qualification remains **partial**:
Valencia's rapid-scrub regression reproduces approximately 250 ms acquisition/fence
waits in current and pre-change executables. The three failures and phase traces
are retained in the validation artifact. Passing unit and editor/export tests do
not supersede that failure. The blocking seam is `AurelianVulkanSwapchain.AcquireNextImage`,
including its synchronous fence wait; underlying WSI/driver causality is not proven.
A safe nonblocking fix needs retained in-flight image/fence ownership and is a
separate graphics change, not a shorter timeout on a locally destroyed fence.

The current-source suites, native gallery, editor playback and independently
decoded MP4 evidence are recorded in Valencia's `docs/validation/pointer-feedback.json`
and [validation/pointer-feedback.json](validation/pointer-feedback.json).
The gallery checks actual native cursor backend shapes, distinct idle/hover/press
GPU pixels, caption hit messages, caption mouse press/release/cancel messages,
maximize/restore/minimize, resize, and a close rejected by the host.

Synthetic coordinates and posted/sent native messages exercise the real shared
code and native window. They are not a physical mouse/keyboard qualification.
Manual Windows Snap flyout selection, OS accessibility, multi-monitor mixed-DPI
dragging and extended live-resize stress are not claimed as tested. CPU event
handling plus Draw wall time is not an optical input-to-visible measurement.
