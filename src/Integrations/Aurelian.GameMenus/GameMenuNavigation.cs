using InputMan.Core;

namespace Aurelian.GameMenus;

public readonly record struct GameMenuInput(bool Up, bool Down, bool Confirm, bool Back, bool Left = false, bool Right = false);

public enum GameMenuCommandKind
{
    Activate,
    Decrease,
    Increase,
}

/// <summary>A navigation result: activate an entry, or step an adjustable entry's value.</summary>
public readonly record struct GameMenuCommand(string Id, GameMenuCommandKind Kind);

public static class GameMenuBindings
{
    public static ActionMapId Map { get; } = new("Aurelian.Menu");
    private static readonly ActionId Up = new("Aurelian.Menu.Up");
    private static readonly ActionId Down = new("Aurelian.Menu.Down");
    private static readonly ActionId Confirm = new("Aurelian.Menu.Confirm");
    private static readonly ActionId Back = new("Aurelian.Menu.Back");
    private static readonly ActionId Left = new("Aurelian.Menu.Left");
    private static readonly ActionId Right = new("Aurelian.Menu.Right");

    public static IReadOnlyList<Binding> CreateBindings()
    {
        return
        [
            Bind.Action(Controls.Key(KeyboardKey.ArrowUp), Up, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Key(KeyboardKey.ArrowDown), Down, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Key(KeyboardKey.Enter), Confirm, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Key(KeyboardKey.Space), Confirm, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Key(KeyboardKey.Escape), Back, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Key(KeyboardKey.ArrowLeft), Left, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Key(KeyboardKey.ArrowRight), Right, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Gamepad(GamepadButton.DpadUp), Up, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Gamepad(GamepadButton.DpadDown), Down, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Gamepad(GamepadButton.DpadLeft), Left, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Gamepad(GamepadButton.DpadRight), Right, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Gamepad(GamepadButton.South), Confirm, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Gamepad(GamepadButton.East), Back, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Gamepad(GamepadButton.Start), Back, consume: ConsumeMode.ControlOnly),
        ];
    }

    public static GameMenuInput Read(InputFrame frame)
    {
        return new(frame.WasPressed(Up), frame.WasPressed(Down), frame.WasPressed(Confirm), frame.WasPressed(Back),
            frame.WasPressed(Left), frame.WasPressed(Right));
    }
}

public sealed class GameMenuNavigation
{
    public int SelectedIndex { get; private set; }

    public string? Update(IReadOnlyList<GameMenuEntry> entries, GameMenuInput input)
    {
        GameMenuTemplate.ValidateEntries(entries);
        EnsureEnabled(entries);
        if (input.Up != input.Down)
        {
            Move(entries, input.Down ? 1 : -1);
        }
        if (input.Confirm && entries.Count > 0 && !entries[SelectedIndex].Disabled)
        {
            return entries[SelectedIndex].Id;
        }
        return null;
    }

    /// <summary>
    /// Like <see cref="Update"/>, but also reports left/right steps on adjustable entries.
    /// Confirm on an adjustable entry steps its value forward.
    /// </summary>
    public GameMenuCommand? Navigate(IReadOnlyList<GameMenuEntry> entries, GameMenuInput input)
    {
        GameMenuTemplate.ValidateEntries(entries);
        EnsureEnabled(entries);
        if (input.Up != input.Down)
        {
            Move(entries, input.Down ? 1 : -1);
        }
        if (entries.Count == 0 || entries[SelectedIndex].Disabled)
        {
            return null;
        }
        GameMenuEntry selected = entries[SelectedIndex];
        if (selected.Adjustable && input.Left != input.Right)
        {
            return new GameMenuCommand(selected.Id, input.Left ? GameMenuCommandKind.Decrease : GameMenuCommandKind.Increase);
        }
        if (input.Confirm)
        {
            GameMenuCommandKind kind = selected.Adjustable ? GameMenuCommandKind.Increase : GameMenuCommandKind.Activate;
            return new GameMenuCommand(selected.Id, kind);
        }
        return null;
    }

    /// <summary>Select an entry by identity, for pointer hover/press feedback and restored menus.</summary>
    public void Select(IReadOnlyList<GameMenuEntry> entries, string id)
    {
        for (int index = 0; index < entries.Count; index++)
        {
            if (entries[index].Id == id && !entries[index].Disabled)
            {
                SelectedIndex = index;
                return;
            }
        }
    }

    public void Reset()
    {
        SelectedIndex = 0;
    }

    private void EnsureEnabled(IReadOnlyList<GameMenuEntry> entries)
    {
        SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, entries.Count - 1));
        if (entries.Count == 0 || !entries[SelectedIndex].Disabled)
        {
            return;
        }
        for (int index = 0; index < entries.Count; index++)
        {
            if (!entries[index].Disabled)
            {
                SelectedIndex = index;
                return;
            }
        }
    }

    private void Move(IReadOnlyList<GameMenuEntry> entries, int direction)
    {
        for (int index = SelectedIndex + direction; index >= 0 && index < entries.Count; index += direction)
        {
            if (!entries[index].Disabled)
            {
                SelectedIndex = index;
                return;
            }
        }
    }
}
