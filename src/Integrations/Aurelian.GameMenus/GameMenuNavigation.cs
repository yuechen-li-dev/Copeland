using InputMan.Core;

namespace Aurelian.GameMenus;

public readonly record struct GameMenuInput(bool Up, bool Down, bool Confirm, bool Back);

public static class GameMenuBindings
{
    public static ActionMapId Map { get; } = new("Aurelian.Menu");
    private static readonly ActionId Up = new("Aurelian.Menu.Up");
    private static readonly ActionId Down = new("Aurelian.Menu.Down");
    private static readonly ActionId Confirm = new("Aurelian.Menu.Confirm");
    private static readonly ActionId Back = new("Aurelian.Menu.Back");

    public static IReadOnlyList<Binding> CreateBindings()
    {
        return
        [
            Bind.Action(Controls.Key(KeyboardKey.ArrowUp), Up, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Key(KeyboardKey.ArrowDown), Down, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Key(KeyboardKey.Enter), Confirm, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Key(KeyboardKey.Space), Confirm, consume: ConsumeMode.ControlOnly),
            Bind.Action(Controls.Key(KeyboardKey.Escape), Back, consume: ConsumeMode.ControlOnly),
        ];
    }

    public static GameMenuInput Read(InputFrame frame)
    {
        return new(frame.WasPressed(Up), frame.WasPressed(Down), frame.WasPressed(Confirm), frame.WasPressed(Back));
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
