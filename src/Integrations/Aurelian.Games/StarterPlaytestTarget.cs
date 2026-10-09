using System.Text.Json;
using Aurelian.GameMenus;
using Aurelian.Playtesting;
using InputMan.Core;
using Machina.Runtime.Input;

namespace Aurelian.Games;

public sealed class StarterPlaytestTarget : IPlaytestTarget<StarterObservation>, IReplayablePlaytestTarget
{
    private readonly StarterGame game;
    private readonly GameMenuView menus;
    private readonly Action<string?>? present;
    private readonly StarterSnapshot initial;
    private readonly Dictionary<string, string> marks = new(StringComparer.Ordinal);
    private int width = 960;
    private int height = 600;
    private bool focused = true;

    public StarterPlaytestTarget(StarterGame game, GameMenuView? menus = null, Action<string?>? present = null)
    {
        this.game = game;
        this.menus = menus ?? new();
        this.present = present;
        initial = game.Capture();
    }

    public bool Quit => game.Quit;
    public void Key(KeyboardKey key, bool down) => game.Controls.Adapter.RecordButton(Controls.Key(key), down);
    public void MouseButton(MouseButton button, bool down) => game.Controls.Adapter.RecordButton(Controls.Mouse(button), down);
    public void MouseDelta(double x, double y)
    {
        game.Controls.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaX), (float)x);
        game.Controls.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaY), (float)y);
    }

    public void Frame(TimeSpan elapsed)
    {
        game.Advance(elapsed);
        if (elapsed > TimeSpan.Zero) present?.Invoke(null);
    }

    public void Pointer(double x, double y, bool? down = null)
    {
        if (down is not { } pressed || !focused) return;
        if (game.Menu is { } menu)
        {
            string? action = menus.Pointer(menu, game.SelectedIndex, new PointerPoint(x * width, y * height), pressed, width, height);
            if (action is not null) game.Activate(action);
        }
        else
        {
            MouseButton(InputMan.Core.MouseButton.Primary, pressed);
        }
    }

    public (double X, double Y) ActionPoint(string action)
    {
        var menu = game.Menu ?? throw new InvalidOperationException("No game menu is open.");
        PointerPoint point = menus.ActionCenter(menu, game.SelectedIndex, action, width, height);
        return (point.X / width, point.Y / height);
    }

    public void Focus(bool focused)
    {
        this.focused = focused;
        menus.CancelPointer();
        if (!focused) game.Pause();
        game.Controls.Adapter.OnFocusChanged(focused);
        game.Audio.SetFocused(focused);
    }

    public void Resize(int width, int height)
    {
        if (present is not null && (width != this.width || height != this.height))
            throw new NotSupportedException("The native starter currently uses a fixed-size Vulkan surface.");
        if (width < 640 || height < 600) throw new ArgumentOutOfRangeException(nameof(width));
        this.width = width;
        this.height = height;
        menus.CancelPointer();
    }

    public void ReleaseMouse()
    {
        foreach (MouseButton button in Enum.GetValues<MouseButton>()) MouseButton(button, false);
        menus.CancelPointer();
    }

    public void Capture(string path)
    {
        if (present is null) throw new NotSupportedException("Pixel capture requires Vulkan.");
        present(path);
    }

    public void Command(string command)
    {
        string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1 && words[0] == "inspect") return;
        if (words.Length == 2 && words[0] == "save") game.SaveAsync(words[1]).GetAwaiter().GetResult();
        else if (words.Length == 2 && words[0] == "load") game.LoadAsync(words[1]).GetAwaiter().GetResult();
        else if (words.Length == 2 && words[0] == "mark") marks[words[1]] = StateJson();
        else if (words.Length == 2 && words[0] == "assert-mark")
        {
            if (!marks.TryGetValue(words[1], out string? expected) || expected != StateJson())
                throw new InvalidOperationException("State does not match mark: " + words[1]);
        }
        else if (words.Length == 3 && words[0] == "assert")
        {
            StarterObservation state = game.Observe();
            string value = words[1] switch
            {
                "screen" => state.Screen,
                "view" => state.View.ToString(),
                "ammo" => state.Ammo.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "shots" => state.Shots.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "hits" => state.Hits.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "motion" => state.Motion ?? "None",
                "gait" => state.Gait?.ToString() ?? "None",
                _ => throw new ArgumentException("Unknown assertion field: " + words[1]),
            };
            if (value != words[2]) throw new InvalidOperationException($"Expected {words[1]}={words[2]}, observed {value}.");
        }
        else throw new ArgumentException("Unknown starter command: " + command);
    }

    public StarterObservation Observe() => game.Observe();
    public void Text(string text) => throw new NotSupportedException("The starter has no text field.");
    public void Scroll(double x, double y, float delta) => throw new NotSupportedException("The starter has no scroll action.");
    public void SettlePersistence() { }

    public void ResetForReplay()
    {
        game.Restore(initial);
        game.ReturnToTitle();
        focused = true;
        menus.CancelPointer();
    }

    private string StateJson() => JsonSerializer.Serialize(game.Capture(), StarterJsonContext.Default.StarterSnapshot);
}
