using System.Text.Json.Serialization;

namespace TinyFarm.Core;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MoveIntent), "move")]
[JsonDerivedType(typeof(NavigateToAnchorIntent), "navigate-anchor")]
[JsonDerivedType(typeof(AnchorReachedIntent), "anchor-reached")]
[JsonDerivedType(typeof(AnchorNavigationFailedIntent), "anchor-failed")]
[JsonDerivedType(typeof(SpatialMoveIntent), "spatial-move")]
[JsonDerivedType(typeof(InteractIntent), "interact")]
[JsonDerivedType(typeof(LookIntent), "look")]
[JsonDerivedType(typeof(TalkIntent), "talk")]
[JsonDerivedType(typeof(TakeIntent), "take")]
[JsonDerivedType(typeof(GiveIntent), "give")]
[JsonDerivedType(typeof(BuyIntent), "buy-item")]
[JsonDerivedType(typeof(SellIntent), "sell-item")]
[JsonDerivedType(typeof(BuyProductIntent), "buy-product")]
[JsonDerivedType(typeof(SellProductIntent), "sell-product")]
[JsonDerivedType(typeof(PlantIntent), "plant")]
[JsonDerivedType(typeof(WaterIntent), "water")]
[JsonDerivedType(typeof(HarvestIntent), "harvest")]
[JsonDerivedType(typeof(GatherIntent), "gather")]
[JsonDerivedType(typeof(CookIntent), "cook")]
[JsonDerivedType(typeof(ChopIntent), "chop")]
[JsonDerivedType(typeof(AttackIntent), "attack")]
[JsonDerivedType(typeof(SelectHotbarSlotIntent), "select-hotbar")]
[JsonDerivedType(typeof(UseSelectedIntent), "use-selected")]
[JsonDerivedType(typeof(WaitIntent), "wait")]
public abstract record GameIntent;

public sealed record MoveIntent(LocationId Destination) : GameIntent;

public sealed record NavigateToAnchorIntent(SceneAnchorId Anchor) : GameIntent;

public sealed record AnchorReachedIntent(SceneAnchorId Anchor) : GameIntent;

public sealed record AnchorNavigationFailedIntent(SceneAnchorId Anchor, string Detail) : GameIntent;

public sealed record SpatialMoveIntent(int DeltaX, int DeltaY, int Distance = 1) : GameIntent;

public sealed record InteractIntent(SceneObjectId? Target = null) : GameIntent;

public sealed record LookIntent : GameIntent;

public sealed record TalkIntent(ActorId Target) : GameIntent;

public sealed record TakeIntent(ItemId Item) : GameIntent;

public sealed record GiveIntent(ItemId Item, ActorId Target) : GameIntent;

public sealed record BuyIntent(ItemId Item) : GameIntent;

public sealed record SellIntent(ItemId Item) : GameIntent;

public sealed record BuyProductIntent(ProductId Product) : GameIntent;

public sealed record SellProductIntent(ProductId Product) : GameIntent;

public sealed record PlantIntent(FarmPlotId Plot, CropId Crop) : GameIntent;

public sealed record WaterIntent(FarmPlotId Plot) : GameIntent;

public sealed record HarvestIntent(FarmPlotId Plot) : GameIntent;

public sealed record GatherIntent(ForageNodeId Node) : GameIntent;

public sealed record CookIntent(SceneObjectId Station, CookingRecipeId Recipe) : GameIntent;

public sealed record ChopIntent(TreeId Tree) : GameIntent;

public sealed record AttackIntent(EnemyId Enemy) : GameIntent;

public sealed record SelectHotbarSlotIntent(HotbarSlotId Slot) : GameIntent;

public sealed record UseSelectedIntent : GameIntent;

public sealed record WaitIntent(int Minutes) : GameIntent;

public enum IntentSourceKind
{
    Human,
    Dominatus,
    Replay
}

public sealed record IntentEnvelope(ActorId Actor, GameIntent Intent, int SubmittedAt, long Sequence, IntentSourceKind Source);

public enum IntentResultStatus
{
    Accepted,
    Rejected,
    NoOp
}

public enum IntentReason
{
    None,
    UnknownActor,
    UnknownTarget,
    UnknownItem,
    UnknownCrop,
    UnknownPlot,
    NotAdjacent,
    TargetAbsent,
    ItemAbsent,
    ItemNotOwned,
    NotForSale,
    InsufficientFunds,
    StoreClosed,
    InvalidWait,
    AlreadyThere,
    PlotOccupied,
    PlotEmpty,
    WrongLocation,
    CropImmature,
    AlreadyWatered,
    StockUnavailable,
    InvalidMovement,
    MovementBlocked,
    NoInteraction,
    NoInteractionTarget,
    NavigationFailed,
    MissingAnchor,
    AnchorUnreachable,
    InvalidAnchorRealization,
    InvalidHotbarSlot,
    ItemOutOfRange,
    ItemNotGround,
    NoSelectedBinding,
    SelectedBindingUnavailable,
    UnsupportedSelectedUse,
    WrongTargetKind,
    UnknownForageNode,
    ForageWrongScene,
    ForageOutOfRange,
    AlreadyDepleted,
    UnknownRecipe,
    WrongStation,
    StationWrongScene,
    StationOutOfRange,
    MissingIngredient,
    UnknownTree,
    TreeWrongScene,
    TreeOutOfRange,
    MissingAxe,
    WrongTool,
    UnknownEnemy,
    EnemyWrongScene,
    EnemyOutOfRange,
    MissingSword,
    WrongWeapon,
    AlreadyDefeated
}

public enum GameEventKind
{
    Looked,
    ActorMoved,
    Conversation,
    ItemTaken,
    ItemGiven,
    ItemBought,
    ItemSold,
    TimeAdvanced,
    FavorAdvanced,
    CropPlanted,
    PlotWatered,
    CropAdvanced,
    CropHarvested,
    DayStarted,
    ShopRestocked,
    SceneExited,
    SceneEntered,
    InteractionTargeted,
    AnchorReached,
    HotbarSlotSelected,
    ForageGathered,
    RecipeCooked,
    TreeChopped,
    EnemyDefeated
}

public enum DialogueTopic
{
    Greeting,
    RequestLetterDelivery,
    EliasReceivesLetter,
    FavorThanks,
    ShopGreeting,
    HarvestComment,
    WeekComment
}

public sealed record GameEvent(
    GameEventKind Kind,
    ActorId Actor,
    ActorId? Target = null,
    ItemId? Item = null,
    LocationId? Location = null,
    int Amount = 0,
    DialogueTopic? Dialogue = null,
    FavorStage? Favor = null,
    ProductId? Product = null,
    CropId? Crop = null,
    FarmPlotId? Plot = null,
    int? Day = null,
    SceneId? Scene = null,
    SceneRouteId? Route = null,
    SceneObjectId? SceneObject = null,
    SceneAnchorId? Anchor = null,
    ForageNodeId? ForageNode = null,
    CookingRecipeId? Recipe = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    TreeId? Tree = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    EnemyId? Enemy = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    EnemyKind? EnemyKind = null);

public sealed record IntentResult(IntentEnvelope Envelope, IntentResultStatus Status, IntentReason Reason, IReadOnlyList<GameEvent> Events);

public sealed record ResolutionBatchResult(TinyFarmState State, IReadOnlyList<IntentResult> Results);
