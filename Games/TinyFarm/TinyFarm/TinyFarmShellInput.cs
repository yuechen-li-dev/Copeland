using System.Numerics;
using Aurelian.Games;
using InputMan.Core;
using TinyFarm.InputMan;
using Controls = TinyFarm.InputMan.GameControls;

namespace TinyFarm;

/// <summary>
/// TinyFarm's controls as shell actions. The shell owns devices, rebinding and the pause menu; four contexts mirror
/// TinyFarm's InputMan maps: free play, its own menus (inventory, stats, stove, chest), dialogue and the pockets search
/// box. The modal contexts claim Escape, so it closes TinyFarm's screen instead of pausing. The layout reads the
/// pointer: menus are clickable, left click uses the selected tool, right click walks to a spot (and uses what is there),
/// and a click advances dialogue.
/// </summary>
public static class TinyFarmShellInput
{
    public const string Menu = "menu";
    public const string Dialogue = "dialogue";
    public const string Text = "text";

    public static GameInputLayout Layout { get; } = new(
    [
        new("move.up", "Move up", GameControl.Key(KeyboardKey.W), GameControl.Key(KeyboardKey.ArrowUp)),
        new("move.down", "Move down", GameControl.Key(KeyboardKey.S), GameControl.Key(KeyboardKey.ArrowDown)),
        new("move.left", "Move left", GameControl.Key(KeyboardKey.A), GameControl.Key(KeyboardKey.ArrowLeft)),
        new("move.right", "Move right", GameControl.Key(KeyboardKey.D), GameControl.Key(KeyboardKey.ArrowRight)),
        new("interact", "Talk / use / enter", GameControl.Key(KeyboardKey.E)) { Gamepad = GameControl.Gamepad(GamepadButton.South) },
        new("use-tool", "Use selected tool", GameControl.Key(KeyboardKey.K), GameControl.Mouse(MouseButton.Primary)) { Gamepad = GameControl.Gamepad(GamepadButton.North) },
        new("sword", "Swing sword", GameControl.Key(KeyboardKey.J)) { Gamepad = GameControl.Gamepad(GamepadButton.West) },
        new("dodge", "Dodge", GameControl.Key(KeyboardKey.Space)) { Gamepad = GameControl.Gamepad(GamepadButton.East) },
        new("eat", "Eat broth", GameControl.Key(KeyboardKey.R)) { Gamepad = GameControl.Gamepad(GamepadButton.RightShoulder) },
        new("sleep", "Sleep", GameControl.Key(KeyboardKey.B)),
        new("inventory", "Pockets", GameControl.Key(KeyboardKey.I)) { Gamepad = GameControl.Gamepad(GamepadButton.Back) },
        new("stats", "Character", GameControl.Key(KeyboardKey.C)) { Gamepad = GameControl.Gamepad(GamepadButton.LeftShoulder) },
        new("hotbar.1", "Tool 1", GameControl.Key(KeyboardKey.Number1)) { Gamepad = GameControl.Gamepad(GamepadButton.DpadUp) },
        new("hotbar.2", "Tool 2", GameControl.Key(KeyboardKey.Number2)) { Gamepad = GameControl.Gamepad(GamepadButton.DpadRight) },
        new("hotbar.3", "Tool 3", GameControl.Key(KeyboardKey.Number3)) { Gamepad = GameControl.Gamepad(GamepadButton.DpadDown) },
        new("hotbar.4", "Tool 4", GameControl.Key(KeyboardKey.Number4)) { Gamepad = GameControl.Gamepad(GamepadButton.DpadLeft) },
        new("toggle-hud", "Show / hide HUD", GameControl.Key(KeyboardKey.F9), Rebindable: false),
        new("toggle-inspector", "Show / hide inspector", GameControl.Key(KeyboardKey.F10), Rebindable: false),

        new("menu.up", "Menu up", GameControl.Key(KeyboardKey.ArrowUp), GameControl.Key(KeyboardKey.W), Rebindable: false) { Gamepad = GameControl.Gamepad(GamepadButton.DpadUp), Context = Menu },
        new("menu.down", "Menu down", GameControl.Key(KeyboardKey.ArrowDown), GameControl.Key(KeyboardKey.S), Rebindable: false) { Gamepad = GameControl.Gamepad(GamepadButton.DpadDown), Context = Menu },
        new("menu.left", "Menu left", GameControl.Key(KeyboardKey.ArrowLeft), GameControl.Key(KeyboardKey.A), Rebindable: false) { Gamepad = GameControl.Gamepad(GamepadButton.DpadLeft), Context = Menu },
        new("menu.right", "Menu right", GameControl.Key(KeyboardKey.ArrowRight), GameControl.Key(KeyboardKey.D), Rebindable: false) { Gamepad = GameControl.Gamepad(GamepadButton.DpadRight), Context = Menu },
        new("menu.confirm", "Menu confirm", GameControl.Key(KeyboardKey.Enter), GameControl.Key(KeyboardKey.E), Rebindable: false) { Gamepad = GameControl.Gamepad(GamepadButton.South), Context = Menu },
        new("menu.close", "Close menu", GameControl.Key(KeyboardKey.Escape), GameControl.Key(KeyboardKey.I), Rebindable: false) { Gamepad = GameControl.Gamepad(GamepadButton.East), Context = Menu },
        new("menu.close-stats", "Close character", GameControl.Key(KeyboardKey.C), Rebindable: false) { Context = Menu },

        new("dialogue.advance", "Next line", GameControl.Key(KeyboardKey.Space), GameControl.Mouse(MouseButton.Primary),
            Rebindable: false) { Gamepad = GameControl.Gamepad(GamepadButton.South), Context = Dialogue },
        new("dialogue.up", "Previous choice", GameControl.Key(KeyboardKey.ArrowUp), GameControl.Key(KeyboardKey.W), Rebindable: false) { Gamepad = GameControl.Gamepad(GamepadButton.DpadUp), Context = Dialogue },
        new("dialogue.down", "Next choice", GameControl.Key(KeyboardKey.ArrowDown), GameControl.Key(KeyboardKey.S), Rebindable: false) { Gamepad = GameControl.Gamepad(GamepadButton.DpadDown), Context = Dialogue },
        new("dialogue.confirm", "Choose", GameControl.Key(KeyboardKey.E), GameControl.Key(KeyboardKey.Enter), Rebindable: false) { Context = Dialogue },
        new("dialogue.cancel", "Leave conversation", GameControl.Key(KeyboardKey.Escape), Rebindable: false) { Gamepad = GameControl.Gamepad(GamepadButton.East), Context = Dialogue },

        // While the search box has focus, letters are text; only Enter and Escape leave it.
        new("text.finish", "Finish search", GameControl.Key(KeyboardKey.Enter), GameControl.Key(KeyboardKey.Escape), Rebindable: false)
        {
            Context = Text,
            Gamepad = GameControl.Gamepad(GamepadButton.East),
        },
    ])
    {
        Pointer = true,
        Contexts =
        [
            new GameInputContext(GameInputContext.Play),
            new GameInputContext(Menu, Priority: 1, Modal: true),
            new GameInputContext(Dialogue, Priority: 2, Modal: true),
            new GameInputContext(Text, Priority: 3, Modal: true),
        ],
    };

    // Shell action -> TinyFarm's own logical action. Several shell actions may feed one TinyFarm action.
    private static readonly (string Shell, ActionId TinyFarm)[] Actions =
    [
        ("interact", Controls.Interact),
        ("use-tool", Controls.UseSelected),
        ("sword", Controls.Sword),
        ("dodge", Controls.Dodge),
        ("eat", Controls.Eat),
        ("sleep", Controls.Sleep),
        ("inventory", Controls.ToggleInventory),
        ("stats", Controls.ToggleStats),
        ("hotbar.1", Controls.Hotbar1),
        ("hotbar.2", Controls.Hotbar2),
        ("hotbar.3", Controls.Hotbar3),
        ("hotbar.4", Controls.Hotbar4),
        ("toggle-hud", Controls.ToggleHud),
        ("toggle-inspector", Controls.ToggleInspector),
        ("menu.up", Controls.UiUp),
        ("menu.down", Controls.UiDown),
        ("menu.left", Controls.UiLeft),
        ("menu.right", Controls.UiRight),
        ("menu.confirm", Controls.UiConfirm),
        ("menu.close", Controls.UiCancel),
        ("menu.close-stats", Controls.UiCancel),
        ("dialogue.advance", Controls.DialogueAdvance),
        ("dialogue.up", Controls.DialogueChoiceUp),
        ("dialogue.down", Controls.DialogueChoiceDown),
        ("dialogue.confirm", Controls.DialogueConfirm),
        ("dialogue.cancel", Controls.DialogueCancel),
        ("text.finish", Controls.UiSearchFinish),
    ];

    /// <summary>
    /// Translate one shell tick into the InputMan frame TinyFarm's game logic reads. The shell has already latched taps
    /// between ticks, so a press reaches exactly one frame.
    /// </summary>
    /// <param name="extraPresses">TinyFarm actions the app raises itself this tick (for example a hotbar slot picked
    /// with the mouse wheel).</param>
    /// <param name="walk">Movement chosen by click-to-go, replacing the keys and stick while it lasts.</param>
    public static InputFrame Frame(GameInputState input, long sequence, float seconds,
        IReadOnlyCollection<ActionId>? extraPresses = null, Vector2? walk = null)
    {
        var actions = new Dictionary<ActionId, ActionValue>();
        foreach (ActionId extra in extraPresses ?? [])
        {
            actions[extra] = ActionValue.Pressed;
        }
        foreach ((string shell, ActionId tinyFarm) in Actions)
        {
            ActionValue value = input.WasPressed(shell) ? ActionValue.Pressed
                : input.IsDown(shell) ? ActionValue.Held
                : input.WasReleased(shell) ? ActionValue.Released
                : ActionValue.Up;
            if (value == ActionValue.Up)
            {
                continue;
            }
            // Strongest wins when two shell actions feed the same TinyFarm action.
            if (!actions.TryGetValue(tinyFarm, out ActionValue existing) || Strength(value) > Strength(existing))
            {
                actions[tinyFarm] = value;
            }
        }
        // TinyFarm's move axis is +X right and +Y up (WASD), like InputMan's Wasd helper.
        var move = new Vector2(input.Axis("move.left", "move.right"), input.Axis("move.down", "move.up")) + input.LeftStick;
        if (walk is { } steered)
        {
            move = steered;
        }
        if (move.LengthSquared() > 1)
        {
            move = Vector2.Normalize(move);
        }
        var axes = new Dictionary<AxisId, float> { [Controls.MoveX] = move.X, [Controls.MoveY] = move.Y };
        var axes2 = new Dictionary<Axis2Id, Vector2> { [Controls.Move] = move };
        return new InputFrame(sequence, seconds, actions, axes, axes2, lastActiveDevice: null);
    }

    private static int Strength(ActionValue value) => value switch
    {
        ActionValue.Pressed => 3,
        ActionValue.Held => 2,
        ActionValue.Released => 1,
        _ => 0,
    };
}
