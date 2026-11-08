using System.Globalization;
using Machina.Core.Nodes;
using Machina.Layout.Rows;
using TinyFarm.Core;

namespace TinyFarm.InputMan;

public enum TinyFarmUiNode
{
    AgentProperties,
    BagFooter,
    BagPanel,
    BagStatus,
    BagSummary,
    BagTitle,
    Checkpoint,
    Clock,
    ClockSurface,
    Confirmation,
    ContainerBag,
    ContainerBagEmpty,
    ContainerBagLabel,
    ContainerBagPage,
    ContainerChest,
    ContainerChestEmpty,
    ContainerChestLabel,
    ContainerChestPage,
    ContainerHelp,
    ContainerPanel,
    ContainerPolicy,
    ContainerReceipt,
    ContainerStatus,
    ContainerSummary,
    ContainerTitle,
    CraftCost,
    CraftFood,
    CraftHelp,
    CraftInputs,
    CraftKnowledge,
    CraftResult,
    CraftStatus,
    CraftWarning,
    CraftXp,
    Description,
    Dialogue,
    DialogueBody,
    DialogueHint,
    Empty,
    Health,
    HealthText,
    InspectAuthority,
    InspectSampling,
    InspectTitle,
    InspectViewport,
    Inspector,
    InventoryTable,
    ItemCategory,
    ItemDetails,
    ItemName,
    ItemQuantity,
    ItemSlot,
    ItemUse,
    Journal,
    JournalTitle,
    NoSelection,
    OpeningSlice,
    PauseHint,
    PausePanel,
    PauseStatus,
    PauseSubtitle,
    PauseTitle,
    PresentationInspector,
    Prompt,
    PromptSurface,
    PromptText,
    Range,
    SelectedTool,
    SliceStatus,
    SliceStatusText,
    Speaker,
    StatsBoundary,
    StatsEmpty,
    StatsFooter,
    StatsPanel,
    StatsRange,
    StatsScrollThumb,
    StatsScrollTrack,
    StatsSummary,
    StatsTitle,
    StovePanel,
    StoveSummary,
    StoveTitle,
    Supplies,
    TinyFarmMenu,
    TitleCheckpoint,
    TitleControls,
    TitleInvitation,
    TitleName,
    TitleStatus,
    TitleSubtitle,
}

/// <summary>UI identity by authored role or domain key, independently of layout and display text.</summary>
public static class TinyFarmUiIds
{
    public static readonly UiIdFamily<TinyFarmUiNode> Nodes = new("tinyfarm", role => role.ToString());
    public static readonly UiIdFamily<string> Choices = new(Nodes[TinyFarmUiNode.Dialogue].Child("choices"), value => value);
    public static readonly UiIdFamily<int> Objectives = new(Nodes[TinyFarmUiNode.Journal].Child("objectives"), value => value.ToString(CultureInfo.InvariantCulture));
    public static readonly UiIdFamily<int> InspectorDetails = new(Nodes[TinyFarmUiNode.Inspector].Child("details"), value => value.ToString(CultureInfo.InvariantCulture));
    public static readonly UiIdFamily<int> Properties = new(Nodes[TinyFarmUiNode.AgentProperties].Child("properties"), value => value.ToString(CultureInfo.InvariantCulture));
    public static readonly UiIdFamily<ProductId> Ingredients = new("ingredient", value => value.Value);
    private static readonly UiIdFamily<ItemId> Items = new("inventory.item", value => value.Value);
    private static readonly UiIdFamily<ProductId> Products = new("inventory.product", value => value.Value);

    public static NodeId InventoryRow(TinyFarmInventoryKey key)
    {
        return key switch
        {
            TinyFarmInventoryKey.Item item => Items[item.Id],
            TinyFarmInventoryKey.Product product => Products[product.Id],
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
    }
}
