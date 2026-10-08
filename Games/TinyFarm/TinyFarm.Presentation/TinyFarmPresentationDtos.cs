using TinyFarm.Core;
using Ariadne.OptFlow.Presentation;

namespace TinyFarm.Presentation;

public sealed record TinyFarmPresentationSnapshot(
    TinyFarmPlayerUiView PlayerUi,
    int Day,
    string Time,
    string LocationName,
    TinyFarmSimulationMode SimulationMode,
    bool InventoryOpen,
    string Status,
    IReadOnlyList<string> InteractionHints,
    IReadOnlyList<string> Narrative,
    DialoguePresentationSnapshot? Dialogue = null);

public enum TinyFarmUiCommandKind
{
    SelectHotbarSlot,
    ToggleInventory,
    TogglePausePlay,
    ToggleFastForward,
    Wait,
    UseSelected,
    Interact,
    DialogueAdvance,
    DialogueChoiceUp,
    DialogueChoiceDown,
    DialogueConfirm,
    DialogueCancel
}

public sealed record TinyFarmUiCommandDto(TinyFarmUiCommandKind Kind, int? HotbarSlot = null);
