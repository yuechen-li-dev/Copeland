using Machina.Core.Semantics;
using Silk.NET.Input;

namespace Aurelian.Machina;

/// <summary>Non-owning native cursor adapter. Reuses OS standard cursors and changes only on transitions.</summary>
public sealed class AurelianCursor(IInputContext input) : IDisposable
{
    public UiCursor Current { get; private set; } = UiCursor.Auto;
    public int Changes { get; private set; }
    public StandardCursor? NativeShape => input.Mice.FirstOrDefault()?.Cursor.StandardCursor;

    public void Set(UiCursor intent)
    {
        if (Current == intent) return;
        foreach (var mouse in input.Mice)
        {
            var cursor = mouse.Cursor;
            var shape = Map(intent);
            cursor.Type = CursorType.Standard;
            cursor.StandardCursor = cursor.IsSupported(shape) ? shape : StandardCursor.Arrow;
        }
        Current = intent;
        Changes++;
    }

    public static StandardCursor Map(UiCursor intent) => intent switch
    {
        UiCursor.Hand => StandardCursor.Hand,
        UiCursor.IBeam => StandardCursor.IBeam,
        UiCursor.ResizeHorizontal => StandardCursor.HResize,
        UiCursor.ResizeVertical => StandardCursor.VResize,
        UiCursor.ResizeNorthWestSouthEast => StandardCursor.NwseResize,
        UiCursor.ResizeNorthEastSouthWest => StandardCursor.NeswResize,
        UiCursor.Move => StandardCursor.ResizeAll,
        UiCursor.Crosshair => StandardCursor.Crosshair,
        UiCursor.NotAllowed => StandardCursor.NotAllowed,
        UiCursor.Wait => StandardCursor.Wait,
        _ => StandardCursor.Arrow
    };

    public void Dispose() => Set(UiCursor.Arrow);
}
