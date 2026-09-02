namespace TinyFarm.Core;

public sealed class TinyFarmResolver
{
    public ResolutionBatchResult Resolve(
        TinyFarmState initialState,
        IEnumerable<IntentEnvelope> submittedIntents)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentNullException.ThrowIfNull(submittedIntents);

        TinyFarmState state = initialState.DeepCopy();
        var results = new List<IntentResult>();

        IEnumerable<IntentEnvelope> ordered = submittedIntents
            .OrderBy(envelope => envelope.SubmittedAt)
            .ThenBy(envelope => envelope.Sequence)
            .ThenBy(envelope => envelope.Actor.Value, StringComparer.Ordinal);

        foreach (IntentEnvelope envelope in ordered)
        {
            results.Add(ResolveOne(state, envelope));
        }

        return new ResolutionBatchResult(state, results);
    }

    private static IntentResult ResolveOne(TinyFarmState state, IntentEnvelope envelope)
    {
        ActorState? actor = FindActor(state, envelope.Actor);
        if (actor is null)
        {
            return Rejected(envelope, IntentReason.UnknownActor);
        }

        return envelope.Intent switch
        {
            MoveIntent move => ResolveMove(state, actor, envelope, move),
            LookIntent => Accepted(envelope, new GameEvent(GameEventKind.Looked, actor.Id, Location: actor.Location)),
            TalkIntent talk => ResolveTalk(state, actor, envelope, talk),
            TakeIntent take => ResolveTake(state, actor, envelope, take),
            GiveIntent give => ResolveGive(state, actor, envelope, give),
            BuyIntent buy => ResolveBuy(state, actor, envelope, buy),
            SellIntent sell => ResolveSell(state, actor, envelope, sell),
            WaitIntent wait => ResolveWait(state, actor, envelope, wait),
            _ => throw new InvalidOperationException($"Unsupported intent type {envelope.Intent.GetType().Name}.")
        };
    }

    private static IntentResult ResolveMove(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        MoveIntent intent)
    {
        if (!TinyFarmContent.Locations.Any(location => location.Id == intent.Destination))
        {
            return Rejected(envelope, IntentReason.UnknownTarget);
        }

        if (actor.Location == intent.Destination)
        {
            return NoOp(envelope, IntentReason.AlreadyThere);
        }

        LocationDefinition current = TinyFarmContent.Location(actor.Location);
        if (!current.Exits.Contains(intent.Destination))
        {
            return Rejected(envelope, IntentReason.NotAdjacent);
        }

        ReplaceActor(state, actor with { Location = intent.Destination });
        return Accepted(
            envelope,
            new GameEvent(GameEventKind.ActorMoved, actor.Id, Location: intent.Destination));
    }

    private static IntentResult ResolveTalk(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        TalkIntent intent)
    {
        ActorState? target = FindActor(state, intent.Target);
        if (target is null)
        {
            return Rejected(envelope, IntentReason.UnknownTarget);
        }

        if (target.Location != actor.Location)
        {
            return Rejected(envelope, IntentReason.TargetAbsent);
        }

        var events = new List<GameEvent>();
        DialogueTopic topic = intent.Target == TinyFarmIds.Sela
            ? DialogueTopic.ShopGreeting
            : DialogueTopic.Greeting;

        if (intent.Target == TinyFarmIds.Mara && state.Favor == FavorStage.NotStarted)
        {
            TransferItem(state, TinyFarmIds.Letter, target.Id, actor.Id);
            state.Favor = FavorStage.LetterReceived;
            AddFact(state, WorldFact.MaraNeedsDelivery);
            topic = DialogueTopic.RequestLetterDelivery;
            events.Add(new GameEvent(
                GameEventKind.FavorAdvanced,
                actor.Id,
                target.Id,
                TinyFarmIds.Letter,
                Favor: FavorStage.LetterReceived));
        }
        else if (intent.Target == TinyFarmIds.Mara && state.Favor == FavorStage.LetterDelivered)
        {
            ReplaceActor(state, actor with { Money = actor.Money + 3 });
            state.Favor = FavorStage.Complete;
            AddFact(state, WorldFact.MaraThankedPlayer);
            topic = DialogueTopic.FavorThanks;
            events.Add(new GameEvent(
                GameEventKind.FavorAdvanced,
                actor.Id,
                target.Id,
                Amount: 3,
                Favor: FavorStage.Complete));
        }

        events.Insert(0, new GameEvent(
            GameEventKind.Conversation,
            actor.Id,
            target.Id,
            Dialogue: topic));
        return Accepted(envelope, events);
    }

    private static IntentResult ResolveTake(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        TakeIntent intent)
    {
        ItemState? item = FindItem(state, intent.Item);
        if (item is null)
        {
            return Rejected(envelope, IntentReason.UnknownItem);
        }

        if (item.Owner is not null || item.GroundLocation != actor.Location)
        {
            return Rejected(envelope, IntentReason.ItemAbsent);
        }

        ReplaceItem(state, item with { GroundLocation = null, Owner = actor.Id });
        AddInventoryItem(state, actor.Id, item.Id);
        return Accepted(envelope, new GameEvent(GameEventKind.ItemTaken, actor.Id, Item: item.Id));
    }

    private static IntentResult ResolveGive(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        GiveIntent intent)
    {
        ActorState? target = FindActor(state, intent.Target);
        if (target is null)
        {
            return Rejected(envelope, IntentReason.UnknownTarget);
        }

        if (target.Location != actor.Location)
        {
            return Rejected(envelope, IntentReason.TargetAbsent);
        }

        ItemState? item = FindItem(state, intent.Item);
        if (item is null)
        {
            return Rejected(envelope, IntentReason.UnknownItem);
        }

        if (item.Owner != actor.Id || !actor.Inventory.Contains(item.Id))
        {
            return Rejected(envelope, IntentReason.ItemNotOwned);
        }

        TransferItem(state, item.Id, actor.Id, target.Id);
        var events = new List<GameEvent>
        {
            new(GameEventKind.ItemGiven, actor.Id, target.Id, item.Id)
        };

        if (item.Id == TinyFarmIds.Letter && target.Id == TinyFarmIds.Elias && state.Favor == FavorStage.LetterReceived)
        {
            state.Favor = FavorStage.LetterDelivered;
            AddFact(state, WorldFact.EliasHasLetter);
            events.Add(new GameEvent(
                GameEventKind.Conversation,
                actor.Id,
                target.Id,
                item.Id,
                Dialogue: DialogueTopic.EliasReceivesLetter));
            events.Add(new GameEvent(
                GameEventKind.FavorAdvanced,
                actor.Id,
                target.Id,
                item.Id,
                Favor: FavorStage.LetterDelivered));
        }

        return Accepted(envelope, events);
    }

    private static IntentResult ResolveBuy(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        BuyIntent intent)
    {
        if (!StoreIsOpen(state.Minute))
        {
            return Rejected(envelope, IntentReason.StoreClosed);
        }

        ActorState shopkeeper = state.Actor(TinyFarmIds.Sela);
        if (actor.Location != shopkeeper.Location)
        {
            return Rejected(envelope, IntentReason.TargetAbsent);
        }

        ItemState? item = FindItem(state, intent.Item);
        if (item is null)
        {
            return Rejected(envelope, IntentReason.UnknownItem);
        }

        if (item.Owner != shopkeeper.Id)
        {
            return Rejected(envelope, IntentReason.NotForSale);
        }

        if (actor.Money < item.Price)
        {
            return Rejected(envelope, IntentReason.InsufficientFunds);
        }

        ReplaceActor(state, actor with { Money = actor.Money - item.Price });
        ReplaceActor(state, shopkeeper with { Money = shopkeeper.Money + item.Price });
        TransferItem(state, item.Id, shopkeeper.Id, actor.Id);
        return Accepted(envelope, new GameEvent(
            GameEventKind.ItemBought,
            actor.Id,
            shopkeeper.Id,
            item.Id,
            Amount: item.Price));
    }

    private static IntentResult ResolveSell(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        SellIntent intent)
    {
        if (!StoreIsOpen(state.Minute))
        {
            return Rejected(envelope, IntentReason.StoreClosed);
        }

        ActorState shopkeeper = state.Actor(TinyFarmIds.Sela);
        if (actor.Location != shopkeeper.Location)
        {
            return Rejected(envelope, IntentReason.TargetAbsent);
        }

        ItemState? item = FindItem(state, intent.Item);
        if (item is null)
        {
            return Rejected(envelope, IntentReason.UnknownItem);
        }

        if (item.Owner != actor.Id || !actor.Inventory.Contains(item.Id))
        {
            return Rejected(envelope, IntentReason.ItemNotOwned);
        }

        int value = Math.Max(1, item.Price / 2);
        ReplaceActor(state, actor with { Money = actor.Money + value });
        ReplaceActor(state, shopkeeper with { Money = shopkeeper.Money - value });
        TransferItem(state, item.Id, actor.Id, shopkeeper.Id);
        return Accepted(envelope, new GameEvent(
            GameEventKind.ItemSold,
            actor.Id,
            shopkeeper.Id,
            item.Id,
            Amount: value));
    }

    private static IntentResult ResolveWait(
        TinyFarmState state,
        ActorState actor,
        IntentEnvelope envelope,
        WaitIntent intent)
    {
        if (intent.Minutes <= 0 || intent.Minutes > 240)
        {
            return Rejected(envelope, IntentReason.InvalidWait);
        }

        state.Minute += intent.Minutes;
        return Accepted(envelope, new GameEvent(
            GameEventKind.TimeAdvanced,
            actor.Id,
            Amount: intent.Minutes));
    }

    private static bool StoreIsOpen(int minute)
    {
        int minuteOfDay = minute % (24 * 60);
        return minuteOfDay >= 9 * 60 && minuteOfDay < 18 * 60;
    }

    private static ActorState? FindActor(TinyFarmState state, ActorId id)
    {
        return state.Actors.SingleOrDefault(actor => actor.Id == id);
    }

    private static ItemState? FindItem(TinyFarmState state, ItemId id)
    {
        return state.Items.SingleOrDefault(item => item.Id == id);
    }

    private static void ReplaceActor(TinyFarmState state, ActorState replacement)
    {
        int index = state.MutableActors.FindIndex(actor => actor.Id == replacement.Id);
        state.MutableActors[index] = replacement;
    }

    private static void ReplaceItem(TinyFarmState state, ItemState replacement)
    {
        int index = state.MutableItems.FindIndex(item => item.Id == replacement.Id);
        state.MutableItems[index] = replacement;
    }

    private static void AddInventoryItem(TinyFarmState state, ActorId owner, ItemId item)
    {
        ActorState actor = state.Actor(owner);
        var inventory = actor.Inventory.ToList();
        inventory.Add(item);
        inventory.Sort((left, right) => StringComparer.Ordinal.Compare(left.Value, right.Value));
        ReplaceActor(state, actor with { Inventory = inventory });
    }

    private static void TransferItem(
        TinyFarmState state,
        ItemId itemId,
        ActorId from,
        ActorId to)
    {
        ActorState source = state.Actor(from);
        ActorState target = state.Actor(to);
        var sourceInventory = source.Inventory.Where(item => item != itemId).ToList();
        var targetInventory = target.Inventory.Append(itemId)
            .OrderBy(item => item.Value, StringComparer.Ordinal)
            .ToList();

        ReplaceActor(state, source with { Inventory = sourceInventory });
        ReplaceActor(state, target with { Inventory = targetInventory });
        ReplaceItem(state, state.Item(itemId) with { Owner = to, GroundLocation = null });
    }

    private static void AddFact(TinyFarmState state, WorldFact fact)
    {
        if (!state.Facts.Contains(fact))
        {
            state.MutableFacts.Add(fact);
            state.MutableFacts.Sort();
        }
    }

    private static IntentResult Accepted(IntentEnvelope envelope, params GameEvent[] events)
    {
        return new IntentResult(envelope, IntentResultStatus.Accepted, IntentReason.None, events);
    }

    private static IntentResult Accepted(IntentEnvelope envelope, IReadOnlyList<GameEvent> events)
    {
        return new IntentResult(envelope, IntentResultStatus.Accepted, IntentReason.None, events);
    }

    private static IntentResult Rejected(IntentEnvelope envelope, IntentReason reason)
    {
        return new IntentResult(envelope, IntentResultStatus.Rejected, reason, []);
    }

    private static IntentResult NoOp(IntentEnvelope envelope, IntentReason reason)
    {
        return new IntentResult(envelope, IntentResultStatus.NoOp, reason, []);
    }
}
