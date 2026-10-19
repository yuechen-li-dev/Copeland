using System.Numerics;
using System.Text.Json;
using Aurelian.GameMenus;
using Aurelian.Playtesting;
using InputMan.Core;
using Machina.Runtime.Input;

namespace Aurelian.Games;

/// <summary>
/// Drives any <see cref="GameSession"/> through the shared playtest runner: physical keys and mouse,
/// menu clicks by action id, focus, resize, pixel capture (native), save/load and fact assertions.
/// Observations are the session's sorted facts, so a script can assert any fact a game publishes.
/// </summary>
public sealed class GamePlaytestTarget : IPlaytestTarget<SortedDictionary<string, string>>, IReplayablePlaytestTarget
{
    private readonly GameSession session;
    private readonly GameMenuView menus;
    private readonly IGamePlaytestSurface? surface;
    private readonly Dictionary<string, string> marks = new(StringComparer.Ordinal);
    private readonly GameShellSave? initial;
    private readonly GameShellSettings initialSettings;
    private int headlessWidth = 960;
    private int headlessHeight = 600;
    private bool focused = true;

    public GamePlaytestTarget(GameSession session, GameMenuView? menus = null, IGamePlaytestSurface? surface = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.menus = menus ?? new GameMenuView();
        this.surface = surface;
        initial = session.SupportsSaves ? session.CaptureSave() : null;
        initialSettings = session.Settings;
        if (surface is null)
        {
            session.Input.SetPointerSurface(new Vector2(headlessWidth, headlessHeight), new Vector2(headlessWidth, headlessHeight));
        }
    }

    public bool Quit => session.Quit;

    private int LogicalWidth => surface?.LogicalWidth ?? headlessWidth;
    private int LogicalHeight => surface?.LogicalHeight ?? headlessHeight;

    public void Key(KeyboardKey key, bool down) => session.Input.Adapter.RecordButton(Controls.Key(key), down);

    public void MouseButton(MouseButton button, bool down) => session.Input.Adapter.RecordButton(Controls.Mouse(button), down);

    public void MouseDelta(double x, double y)
    {
        session.Input.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaX), (float)x);
        session.Input.Adapter.RecordAxis(Controls.Mouse(MouseAxis.DeltaY), (float)y);
    }

    public void Frame(TimeSpan elapsed)
    {
        session.Advance(elapsed);
        if (elapsed > TimeSpan.Zero)
        {
            surface?.Present(null);
        }
    }

    public void Pointer(double x, double y, bool? down = null)
    {
        if (!focused)
        {
            return;
        }
        if (session.Menu is null && session.Manifest.Input.Pointer)
        {
            // Script coordinates are fractions of the surface; the device reports window units.
            Vector2 window = session.Input.PointerWindow;
            session.Input.Adapter.RecordPointerPosition(new Vector2((float)(x * window.X), (float)(y * window.Y)));
        }
        if (down is not { } pressed)
        {
            return;
        }
        if (session.Menu is { } menu)
        {
            string? action = menus.Pointer(menu, session.SelectedIndex, new PointerPoint(x * LogicalWidth, y * LogicalHeight),
                pressed, LogicalWidth, LogicalHeight);
            if (action is not null)
            {
                session.Activate(action);
            }
        }
        else
        {
            MouseButton(InputMan.Core.MouseButton.Primary, pressed);
        }
    }

    public (double X, double Y) ActionPoint(string action)
    {
        if (session.Menu is null)
        {
            Vector2 pixels = session.Input.PointerSurface;
            return session.Game.ActionPoint(action, new GameViewRequest((int)pixels.X, (int)pixels.Y))
                ?? throw new InvalidOperationException($"No shell menu is open and the game has no clickable '{action}'.");
        }
        GameMenuPage menu = session.Menu;
        PointerPoint point = menus.ActionCenter(menu, session.SelectedIndex, action, LogicalWidth, LogicalHeight);
        return (point.X / LogicalWidth, point.Y / LogicalHeight);
    }

    public void Focus(bool focused)
    {
        this.focused = focused;
        menus.CancelPointer();
        session.OnFocusChanged(focused);
    }

    public void Resize(int width, int height)
    {
        if (width < 640 || height < 480)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Playtest surfaces must be at least 640x480.");
        }
        menus.CancelPointer();
        if (surface is not null)
        {
            surface.Resize(width, height);
            return;
        }
        headlessWidth = Math.Max(960, width);
        headlessHeight = Math.Max(600, height);
        session.Input.SetPointerSurface(new Vector2(headlessWidth, headlessHeight), new Vector2(headlessWidth, headlessHeight));
    }

    public void ReleaseMouse()
    {
        foreach (MouseButton button in Enum.GetValues<MouseButton>())
        {
            MouseButton(button, false);
        }
        menus.CancelPointer();
    }

    public void Capture(string path)
    {
        if (surface is null)
        {
            throw new NotSupportedException("Pixel capture requires the native Vulkan surface.");
        }
        surface.Present(path);
    }

    public void Command(string command)
    {
        string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1 && words[0] == "inspect")
        {
            return;
        }
        if (words.Length == 2 && words[0] == "save")
        {
            session.SaveAsync(words[1]).GetAwaiter().GetResult();
        }
        else if (words.Length == 2 && words[0] == "load")
        {
            session.LoadAsync(words[1]).GetAwaiter().GetResult();
        }
        else if (words.Length == 2 && words[0] == "activate")
        {
            session.Activate(words[1]);
        }
        else if (words.Length == 3 && words[0] == "adjust")
        {
            session.Adjust(words[1], int.Parse(words[2], System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (words.Length == 2 && words[0] == "mark")
        {
            marks[words[1]] = StateJson();
        }
        else if (words.Length == 2 && words[0] == "assert-mark")
        {
            if (!marks.TryGetValue(words[1], out string? expected) || expected != StateJson())
            {
                throw new InvalidOperationException("State does not match mark: " + words[1]);
            }
        }
        else if (words.Length >= 3 && words[0] == "assert")
        {
            string expected = string.Join(' ', words.Skip(2));
            IReadOnlyDictionary<string, string> facts = session.Observe().Values;
            if (!facts.TryGetValue(words[1], out string? actual))
            {
                throw new ArgumentException($"Unknown fact '{words[1]}'. Known facts: {string.Join(", ", facts.Keys)}");
            }
            if (actual != expected)
            {
                throw new InvalidOperationException($"Expected {words[1]}={expected}, observed {actual}.");
            }
        }
        else if (!session.Game.Command(command))
        {
            throw new ArgumentException("Unknown game command: " + command
                + ". Commands: inspect, save S, load S, activate ID, adjust ID +1|-1, mark M, assert-mark M, assert FACT VALUE,"
                + " plus any the game defines.");
        }
    }

    public SortedDictionary<string, string> Observe() => new(session.Observe().Values.ToDictionary(), StringComparer.Ordinal);

    public void Text(string text)
    {
        if (session.Menu is not null || !session.Manifest.Input.Pointer)
        {
            throw new NotSupportedException("Text goes to games that read the pointer and keyboard typing, while playing.");
        }
        session.Input.EnterText(text);
    }

    public void Scroll(double x, double y, float delta)
    {
        if (session.Menu is not null || !session.Manifest.Input.Pointer)
        {
            throw new NotSupportedException("The wheel goes to games that read the pointer, while playing.");
        }
        Pointer(x, y);
        session.Input.Adapter.RecordAxis(Controls.Mouse(MouseAxis.WheelY), delta);
    }

    public void SettlePersistence()
    {
    }

    public void ResetForReplay()
    {
        // Replay must see the preferences the recorded inputs saw; the persisted copy is left alone.
        session.ApplySettings(initialSettings, persist: false);
        if (initial is not null)
        {
            session.RestoreSave(initial);
        }
        else
        {
            session.Game.NewGame();
        }
        session.ReturnToTitle();
        focused = true;
        menus.CancelPointer();
    }

    private string StateJson()
    {
        if (session.SupportsSaves)
        {
            return JsonSerializer.Serialize(session.CaptureSave(), GameShellJsonContext.Default.GameShellSave);
        }
        return JsonSerializer.Serialize(Observe(), GameShellJsonContext.Default.SortedDictionaryStringString);
    }
}

/// <summary>Native presentation hooks used by <see cref="GamePlaytestTarget"/>.</summary>
public interface IGamePlaytestSurface
{
    int LogicalWidth { get; }
    int LogicalHeight { get; }
    void Present(string? capturePath);
    void Resize(int width, int height);
}
