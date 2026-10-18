using System.Numerics;
using Aurelian.Games;
using InputMan.Core;
using TinyFarm.InputMan;
using Controls = TinyFarm.InputMan.GameControls;

namespace TinyFarm;

/// <summary>
/// TinyFarm's controls as shell actions. The shell owns devices, rebinding and the pause menu; three contexts mirror
/// TinyFarm's InputMan maps: free play, its own menus (inventory, stats, stove, chest) and dialogue. The two modal
/// contexts claim Escape, so it closes TinyFarm's screen instead of pausing.
/// </summary>
public static class TinyFarmShellInput
{
    public const string Menu = "menu";
    public const string Dialogue = "dialogue";

    public static GameInputLayout Layout { get; } = new(
    [
        new("move.up", "Move up", GameControl.Key(KeyboardKey.W), GameControl.Key(KeyboardKey.ArrowUp)),
        new("move.down", "Move down", GameControl.Key(KeyboardKey.S), GameControl.Key(KeyboardKey.ArrowDown)),
        new("move.left", "Move left", GameControl.Key(KeyboardKey.A), GameControl.Key(KeyboardKey.ArrowLeft)),
        new("move.right", "Move right", GameControl.Key(KeyboardKey.D), GameControl.Key(KeyboardKey.ArrowRight)),
        new("interact", "Talk / use / enter", GameControl.Key(KeyboardKey.E)),
        new("use-tool", "Use selected tool", GameControl.Key(KeyboardKey.K)),
        new("sword", "Swing sword", GameControl.Key(KeyboardKey.J)),
        new("dodge", "Dodge", GameControl.Key(KeyboardKey.Space)),
        new("eat", "Eat broth", GameControl.Key(KeyboardKey.R)),
        new("sleep", "Sleep", GameControl.Key(KeyboardKey.B)),
        new("inventory", "Pockets", GameControl.Key(KeyboardKey.I)),
        new("stats", "Character", GameControl.Key(KeyboardKey.C)),
        new("hotbar.1", "Tool 1", GameControl.Key(KeyboardKey.Number1)),
        new("hotbar.2", "Tool 2", GameControl.Key(KeyboardKey.Number2)),
        new("hotbar.3", "Tool 3", GameControl.Key(KeyboardKey.Number3)),
        new("hotbar.4", "Tool 4", GameControl.Key(KeyboardKey.Number4)),
        new("toggle-hud", "Show / hide HUD", GameControl.Key(KeyboardKey.F9), Rebindable: false),
        new("toggle-inspector", "Show / hide inspector", GameControl.Key(KeyboardKey.F10), Rebindable: false),

        new("menu.up", "Menu up", GameControl.Key(KeyboardKey.ArrowUp), GameControl.Key(KeyboardKey.W), Rebindable: false) { Context = Menu },
        new("menu.down", "Menu down", GameControl.Key(KeyboardKey.ArrowDown), GameControl.Key(KeyboardKey.S), Rebindable: false) { Context = Menu },
        new("menu.left", "Menu left", GameControl.Key(KeyboardKey.ArrowLeft), GameControl.Key(KeyboardKey.A), Rebindable: false) { Context = Menu },
        new("menu.right", "Menu right", GameControl.Key(KeyboardKey.ArrowRight), GameControl.Key(KeyboardKey.D), Rebindable: false) { Context = Menu },
        new("menu.confirm", "Menu confirm", GameControl.Key(KeyboardKey.Enter), GameControl.Key(KeyboardKey.E), Rebindable: false) { Context = Menu },
        new("menu.close", "Close menu", GameControl.Key(KeyboardKey.Escape), GameControl.Key(KeyboardKey.I), Rebindable: false) { Context = Menu },
        new("menu.close-stats", "Close character", GameControl.Key(KeyboardKey.C), Rebindable: false) { Context = Menu },

        new("dialogue.advance", "Next line", GameControl.Key(KeyboardKey.Space), Rebindable: false) { Context = Dialogue },
        new("dialogue.up", "Previous choice", GameControl.Key(KeyboardKey.ArrowUp), GameControl.Key(KeyboardKey.W), Rebindable: false) { Context = Dialogue },
        new("dialogue.down", "Next choice", GameControl.Key(KeyboardKey.ArrowDown), GameControl.Key(KeyboardKey.S), Rebindable: false) { Context = Dialogue },
        new("dialogue.confirm", "Choose", GameControl.Key(KeyboardKey.E), GameControl.Key(KeyboardKey.Enter), Rebindable: false) { Context = Dialogue },
        new("dialogue.cancel", "Leave conversation", GameControl.Key(KeyboardKey.Escape), Rebindable: false) { Context = Dialogue },
    ])
    {
        Contexts =
        [
            new GameInputContext(GameInputContext.Play),
            new GameInputContext(Menu, Priority: 1, Modal: true),
            new GameInputContext(Dialogue, Priority: 2, Modal: true),
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
    ];

    /// <summary>
    /// Translate one shell tick into the InputMan frame TinyFarm's game logic reads. The shell has already latched taps
    /// between ticks, so a press reaches exactly one frame.
    /// </summary>
    public static InputFrame Frame(GameInputState input, long sequence, float seconds)
    {
        var actions = new Dictionary<ActionId, ActionValue>();
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
        var move = new Vector2(input.Axis("move.left", "move.right"), input.Axis("move.down", "move.up"));
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
