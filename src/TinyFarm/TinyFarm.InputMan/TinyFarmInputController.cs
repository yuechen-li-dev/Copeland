using InputMan.Core;
using TinyFarm.Core;

namespace TinyFarm.InputMan;

public abstract record TinyFarmInputCommand;
public sealed record SubmitGameIntent(GameIntent Intent) : TinyFarmInputCommand;
public sealed record TogglePauseCommand : TinyFarmInputCommand;
public sealed record ToggleInventoryCommand : TinyFarmInputCommand;

/// <summary>App-owned logical-input lowering. Replay authority begins at the emitted semantic intent.</summary>
public sealed class TinyFarmInputController(int movementDistance = ScenePosition.UnitsPerTile / 8)
{
    public IReadOnlyList<TinyFarmInputCommand> Map(InputFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var commands = new List<TinyFarmInputCommand>();
        int x = Math.Sign(frame.GetAxis2(GameControls.Move).X);
        int y = -Math.Sign(frame.GetAxis2(GameControls.Move).Y);
        if (x != 0 || y != 0)
        {
            commands.Add(new SubmitGameIntent(new SpatialMoveIntent(x, y, movementDistance)));
        }
        if (frame.WasPressed(GameControls.Interact)) commands.Add(new SubmitGameIntent(new InteractIntent()));
        if (frame.WasPressed(GameControls.Hotbar1)) commands.Add(new SubmitGameIntent(new SelectHotbarSlotIntent(new HotbarSlotId(1))));
        if (frame.WasPressed(GameControls.Hotbar2)) commands.Add(new SubmitGameIntent(new SelectHotbarSlotIntent(new HotbarSlotId(2))));
        if (frame.WasPressed(GameControls.Pause)) commands.Add(new TogglePauseCommand());
        if (frame.WasPressed(GameControls.ToggleInventory)) commands.Add(new ToggleInventoryCommand());
        return commands;
    }

    public TinyFarmDialogueAction? MapDialogue(InputFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.WasPressed(GameControls.DialogueChoiceUp))
        {
            return TinyFarmDialogueAction.ChoiceUp;
        }
        if (frame.WasPressed(GameControls.DialogueChoiceDown))
        {
            return TinyFarmDialogueAction.ChoiceDown;
        }
        if (frame.WasPressed(GameControls.DialogueCancel))
        {
            return TinyFarmDialogueAction.Cancel;
        }
        if (frame.WasPressed(GameControls.DialogueConfirm))
        {
            return TinyFarmDialogueAction.Confirm;
        }
        if (frame.WasPressed(GameControls.DialogueAdvance))
        {
            return TinyFarmDialogueAction.Advance;
        }
        return null;
    }
}
