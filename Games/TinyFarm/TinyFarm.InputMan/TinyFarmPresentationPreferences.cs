using InputMan.Core;

namespace TinyFarm.InputMan;

/// <summary>Transient application presentation preferences; never serialized into a session.</summary>
public sealed class TinyFarmPresentationPreferences
{
    public bool HudVisible { get; set; } = true;
    public bool InspectorVisible { get; set; }
    public bool CleanCaptureRequested { get; set; }

    /// <summary>
    /// True when a host (the Aurelian game shell) draws the title and pause menus. TinyFarm then keeps its title
    /// painting as the backdrop but does not draw its own title or pause menu over it.
    /// </summary>
    public bool HostMenus { get; set; }

    public void Handle(InputFrame input)
    {
        if (input.WasPressed(GameControls.ToggleHud))
        {
            HudVisible = !HudVisible;
        }
        if (input.WasPressed(GameControls.ToggleInspector))
        {
            InspectorVisible = !InspectorVisible;
        }
        if (input.WasPressed(GameControls.CleanCapture))
        {
            CleanCaptureRequested = true;
        }
    }
}
