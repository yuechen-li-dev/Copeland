namespace TinyFarm.Core;

public abstract record GameIntent;

public sealed record MoveIntent(LocationId Destination) : GameIntent;

public sealed record LookIntent : GameIntent;

public sealed record TalkIntent(ActorId Target) : GameIntent;

public sealed record TakeIntent(ItemId Item) : GameIntent;

public sealed record GiveIntent(ItemId Item, ActorId Target) : GameIntent;

public sealed record BuyIntent(ItemId Item) : GameIntent;

public sealed record SellIntent(ItemId Item) : GameIntent;

public sealed record WaitIntent(int Minutes) : GameIntent;

public enum IntentSourceKind
{
    Human,
    Dominatus,
    Replay
}

public sealed record IntentEnvelope(
    ActorId Actor,
    GameIntent Intent,
    int SubmittedAt,
    long Sequence,
    IntentSourceKind Source);

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
    NotAdjacent,
    TargetAbsent,
    ItemAbsent,
    ItemNotOwned,
    NotForSale,
    InsufficientFunds,
    StoreClosed,
    InvalidWait,
    AlreadyThere
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
    FavorAdvanced
}

public enum DialogueTopic
{
    Greeting,
    RequestLetterDelivery,
    EliasReceivesLetter,
    FavorThanks,
    ShopGreeting
}

public sealed record GameEvent(
    GameEventKind Kind,
    ActorId Actor,
    ActorId? Target = null,
    ItemId? Item = null,
    LocationId? Location = null,
    int Amount = 0,
    DialogueTopic? Dialogue = null,
    FavorStage? Favor = null);

public sealed record IntentResult(
    IntentEnvelope Envelope,
    IntentResultStatus Status,
    IntentReason Reason,
    IReadOnlyList<GameEvent> Events);

public sealed record ResolutionBatchResult(
    TinyFarmState State,
    IReadOnlyList<IntentResult> Results);
