using InputMan.Aurelian;
using Aurelian.GameMenus;
using Aurelian.Games;

namespace Aurelian.Beacon3D;

public readonly record struct BeaconCommands(BeaconInput Movement, bool Pause, GameMenuInput Menu = default);

/// <summary>Beacon maps the engine's shared InputMan command profile to its own domain input.</summary>
public sealed class BeaconControls : IDisposable
{
    private readonly GameControls controls = new();
    public const float MouseSensitivity = 0.0025f;
    public AurelianInputAdapter Adapter => controls.Adapter;

    public BeaconCommands Tick(float seconds, bool menuActive = false)
    {
        GameCommands command = controls.Tick(seconds, menuActive);
        return new BeaconCommands(new BeaconInput(command.Forward, command.Strafe, command.Turn,
            command.Look, command.Jump, command.MouseYaw, command.MousePitch, command.Fire, command.Reload),
            command.Pause, command.Menu);
    }

    public void Dispose() => controls.Dispose();
}
