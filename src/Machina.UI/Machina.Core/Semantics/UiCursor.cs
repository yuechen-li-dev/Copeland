namespace Machina.Core.Semantics;

/// <summary>Backend-neutral native pointer intent. Auto follows the control's semantic role.</summary>
public enum UiCursor
{
    Auto,
    Arrow,
    Hand,
    IBeam,
    ResizeHorizontal,
    ResizeVertical,
    ResizeNorthWestSouthEast,
    ResizeNorthEastSouthWest,
    Move,
    Crosshair,
    NotAllowed,
    Wait
}

public static class UiCursorPolicy
{
    public static UiCursor Resolve(UiSemantics? semantics, bool actionable = false)
    {
        if (semantics?.Disabled == true) return UiCursor.Arrow;
        if (semantics is { Cursor: not UiCursor.Auto }) return semantics.Cursor;
        return semantics?.Role switch
        {
            UiRole.Input or UiRole.SpinButton => UiCursor.IBeam,
            UiRole.Slider => UiCursor.ResizeHorizontal,
            UiRole.Button or UiRole.Link or UiRole.Checkbox or UiRole.Switch or UiRole.Radio
                or UiRole.Tab or UiRole.Option or UiRole.MenuItem or UiRole.ComboBox => UiCursor.Hand,
            _ => actionable ? UiCursor.Hand : UiCursor.Arrow
        };
    }
}
