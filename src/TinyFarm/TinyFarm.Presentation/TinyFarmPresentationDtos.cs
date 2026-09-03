using TinyFarm.Core;

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
    IReadOnlyList<string> Narrative);

public enum TinyFarmUiCommandKind
{
    SelectHotbarSlot,
    ToggleInventory,
    TogglePausePlay,
    ToggleFastForward,
    Wait,
    UseSelected,
    Interact
}

public sealed record TinyFarmUiCommandDto(TinyFarmUiCommandKind Kind, int? HotbarSlot = null);
