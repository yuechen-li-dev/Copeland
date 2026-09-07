using InputMan.Core;

namespace TinyFarm.InputMan;

/// <summary>Transient application presentation preferences; never serialized into a session.</summary>
public sealed class TinyFarmPresentationPreferences
{
    public bool HudVisible { get; set; } = true;
    public bool InspectorVisible { get; set; }
    public bool CleanCaptureRequested { get; set; }

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
