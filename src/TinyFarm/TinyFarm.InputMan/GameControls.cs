using InputMan.Core;

namespace TinyFarm.InputMan;

/// <summary>Application-owned logical control declarations. InputMan remains unaware of game meaning.</summary>
public static class GameControls
{
    public static readonly ActionMapId Gameplay = new("Gameplay");
    public static readonly ActionMapId Ui = new("UI");
    public static readonly ActionMapId Rebind = new("Rebind");

    public static readonly ActionId Interact = new("Interact");
    public static readonly ActionId Pause = new("Pause");
    public static readonly ActionId ToggleInventory = new("ToggleInventory");
    public static readonly ActionId UiConfirm = new("UI.Confirm");
    public static readonly ActionId UiCancel = new("UI.Cancel");
    public static readonly ActionId Hotbar1 = new("Hotbar1");
    public static readonly ActionId Hotbar2 = new("Hotbar2");
    public static readonly AxisId MoveX = new("MoveX");
    public static readonly AxisId MoveY = new("MoveY");
    public static readonly Axis2Id Move = new("Move");

    public static InputProfile CreateProfile()
    {
        List<Binding> gameplayBindings =
        [
            .. Input.Wasd(MoveX, MoveY),
            .. Input.GamepadLeftStick(MoveX, MoveY, deadzone: 0.15f),
            Bind.Action(Controls.Key(KeyboardKey.E), Interact, name: "Interact.Keyboard"),
            Bind.Action(Controls.Gamepad(GamepadButton.South), Interact, name: "Interact.Gamepad"),
            Bind.Action(Controls.Key(KeyboardKey.Escape), Pause, name: "Pause.Keyboard"),
            Bind.Action(Controls.Gamepad(GamepadButton.Start), Pause, name: "Pause.Gamepad"),
            Bind.Action(Controls.Key(KeyboardKey.I), ToggleInventory, name: "Inventory.Keyboard"),
            Bind.Action(Controls.Key(KeyboardKey.Number1), Hotbar1, name: "Hotbar1.Keyboard"),
            Bind.Action(Controls.Key(KeyboardKey.Number2), Hotbar2, name: "Hotbar2.Keyboard"),
            Bind.ActionChord(
                Controls.Key(KeyboardKey.F),
                Interact,
                ButtonEdge.Pressed,
                name: "Interact.AlternateChord",
                modifiers: Controls.Key(KeyboardKey.LeftShift)),
        ];

        return Input.Profile(
            [
                Input.Map(
                    Ui,
                    100,
                    [
                        Bind.Action(Controls.Key(KeyboardKey.E), UiConfirm, consume: ConsumeMode.ControlOnly, name: "Confirm.Keyboard"),
                        Bind.Action(Controls.Gamepad(GamepadButton.South), UiConfirm, consume: ConsumeMode.ControlOnly, name: "Confirm.Gamepad"),
                        Bind.Action(Controls.Key(KeyboardKey.Escape), UiCancel, consume: ConsumeMode.ControlOnly, name: "Cancel.Keyboard"),
                        Bind.Action(Controls.Gamepad(GamepadButton.East), UiCancel, consume: ConsumeMode.ControlOnly, name: "Cancel.Gamepad"),
                    ]),
                Input.Map(Gameplay, 10, gameplayBindings, canConsume: false),
                Input.Map(Rebind, 1000, [], canConsume: true),
            ],
            [Input.Axis2(Move, MoveX, MoveY)]);
    }
}
