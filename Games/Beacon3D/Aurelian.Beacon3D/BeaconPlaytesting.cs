using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.GameMenus;
using Aurelian.Playtesting;
using Aurelian.Runtime.Inspection;
using InputMan.Core;
using Machina.Runtime.Input;

namespace Aurelian.Beacon3D;

public sealed record BeaconPlaytestObservation(string Backend, string Screen, bool Quit, bool Focused,
    float Time, float X, float Z, float Yaw, float Pitch, float Height, int Health, int Ammo,
    int Wave, int Kills, int Shots, long AgentTicks, string SemanticHash, string PolicyHash,
    AgentInspection Brains);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(BeaconPlaytestObservation))]
public partial class BeaconPlaytestJsonContext : JsonSerializerContext;

/// <summary>The same application, InputMan bindings, and menu hit tests used by the native game.</summary>
public sealed class BeaconPlaytestTarget : IPlaytestTarget<BeaconPlaytestObservation>, IReplayablePlaytestTarget, IDisposable
{
    private readonly GameMenuView menus;
    private readonly Action<BeaconApplication, string?>? present;
    private readonly string backend;
    private readonly Dictionary<string, string> marks = new(StringComparer.Ordinal);
    private BeaconControls controls = new();
    private BeaconInput pending;
    private int width;
    private int height;
    private readonly int initialWidth;
    private readonly int initialHeight;
    private bool focused = true;
    private bool pointerPressed;
    private bool disposed;

    public BeaconPlaytestTarget(GameMenuView menus, int width = 960, int height = 600,
        Action<BeaconApplication, string?>? present = null)
    {
        this.menus = menus;
        this.width = width;
        this.height = height;
        initialWidth = width;
        initialHeight = height;
        this.present = present;
        backend = present is null ? "headless-inputman" : "native-vulkan-inputman";
    }

    public BeaconApplication Application { get; private set; } = new(traceCapacity: 4096);
    public bool Quit => Application.ExitRequested;

    public void Key(KeyboardKey key, bool down)
    {
        controls.Adapter.RecordButton(Controls.Key(key), down);
    }

    public void MouseButton(MouseButton button, bool down)
    {
        controls.Adapter.RecordButton(Controls.Mouse(button), down);
    }

    public void MouseDelta(double x, double y)
    {
        controls.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaX), (float)x);
        controls.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaY), (float)y);
    }

    public void Pointer(double x, double y, bool? down = null)
    {
        if (down is not bool pressed || !focused)
        {
            return;
        }
        if (Application.Menu is { } menu)
        {
            string? action = menus.Pointer(menu, Application.SelectedIndex,
                new PointerPoint(x * width, y * height), pressed, width, height);
            if (action is not null)
            {
                Application.Activate(action);
                pending = default;
            }
        }
        else if (pressed || pointerPressed)
        {
            MouseButton(InputMan.Core.MouseButton.Primary, pressed);
        }
        pointerPressed = pressed;
    }

    public void Frame(TimeSpan elapsed)
    {
        float seconds = (float)elapsed.TotalSeconds;
        BeaconCommands commands = controls.Tick(seconds, Application.Menu is not null);
        if (Application.Screen == BeaconScreen.Playing && !commands.Pause)
        {
            BeaconInput movement = commands.Movement;
            movement = movement with
            {
                Jump = movement.Jump || pending.Jump,
                Reload = movement.Reload || pending.Reload,
                Fire = movement.Fire || pending.Fire,
                MouseYaw = movement.MouseYaw + pending.MouseYaw,
                MousePitch = movement.MousePitch + pending.MousePitch,
            };
            pending = seconds == 0 ? movement : default;
            commands = commands with { Movement = movement };
        }
        else
        {
            pending = default;
        }
        Application.Update(commands, seconds);
        if (elapsed > TimeSpan.Zero)
        {
            present?.Invoke(Application, null);
        }
    }

    public void Focus(bool value)
    {
        focused = value;
        controls.Adapter.OnFocusChanged(value);
        menus.CancelPointer();
        pointerPressed = false;
        pending = default;
        if (!value && Application.Screen == BeaconScreen.Playing)
        {
            Application.Update(new BeaconCommands(default, true), 0);
        }
    }

    public void Resize(int newWidth, int newHeight)
    {
        if (present is not null && (newWidth != width || newHeight != height))
        {
            throw new NotSupportedException("Beacon's native Vulkan target is fixed size; headless menu resizing is supported.");
        }
        width = newWidth;
        height = newHeight;
        menus.CancelPointer();
    }

    public void ReleaseMouse()
    {
        foreach (MouseButton button in Enum.GetValues<MouseButton>())
        {
            controls.Adapter.RecordButton(Controls.Mouse(button), false);
        }
        pointerPressed = false;
        menus.CancelPointer();
        pending = pending with { Fire = false };
    }

    public (double X, double Y) ActionPoint(string action)
    {
        if (Application.Menu is not { } menu)
        {
            throw new InvalidOperationException("No menu is open.");
        }
        PointerPoint center = menus.ActionCenter(menu, Application.SelectedIndex, action, width, height);
        return (center.X / width, center.Y / height);
    }

    public void Capture(string path)
    {
        if (present is null)
        {
            throw new NotSupportedException("Pixel capture requires the native Vulkan target.");
        }
        present(Application, path);
    }

    public void Text(string text) => throw new NotSupportedException("Beacon has no text field.");
    public void Scroll(double x, double y, float delta) => throw new NotSupportedException("Beacon has no scroll action.");
    public void SettlePersistence() { }

    public void Command(string command)
    {
        string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (command == "inspect" || command == "inspect brains")
        {
            return;
        }
        if (words.Length == 2 && words[0] == "mark")
        {
            marks[words[1]] = ReplayHash();
            return;
        }
        bool valid = words.Length == 3 && words[0] == "assert" && words[1] switch
        {
            "screen" => Application.Screen.ToString().Equals(words[2], StringComparison.OrdinalIgnoreCase),
            "unchanged" => marks.TryGetValue(words[2], out string? hash) && hash == ReplayHash(),
            "wave" => Application.Game.Wave == int.Parse(words[2], CultureInfo.InvariantCulture),
            "shots" => Application.Game.Shots == int.Parse(words[2], CultureInfo.InvariantCulture),
            _ => false,
        };
        if (!valid)
        {
            throw new InvalidOperationException("Unknown command or failed assertion: " + command);
        }
    }

    private string ReplayHash()
    {
        BeaconPlaytestObservation observation = Observe();
        return Application.Screen + ":" + observation.SemanticHash + ":" + observation.PolicyHash;
    }

    public BeaconPlaytestObservation Observe()
    {
        BeaconGame game = Application.Game;
        AgentInspection brains = game.InspectBrains();
        byte[] policy = JsonSerializer.SerializeToUtf8Bytes(brains, AgentInspectionJsonContext.Default.AgentInspection);
        return new BeaconPlaytestObservation(backend, Application.Screen.ToString(), Quit, focused,
            game.Time, game.Position.X, game.Position.Y, game.Yaw, game.Pitch, game.Height,
            game.Health, game.Ammo, game.Wave, game.Kills, game.Shots, game.AgentTicks,
            game.SemanticHash(), Convert.ToHexString(SHA256.HashData(policy)), brains);
    }

    public void ResetForReplay()
    {
        controls.Dispose();
        controls = new BeaconControls();
        Application.Dispose();
        Application = new BeaconApplication(traceCapacity: 4096);
        pending = default;
        focused = true;
        pointerPressed = false;
        width = initialWidth;
        height = initialHeight;
        menus.CancelPointer();
        marks.Clear();
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            Application.Dispose();
            controls.Dispose();
        }
    }
}
