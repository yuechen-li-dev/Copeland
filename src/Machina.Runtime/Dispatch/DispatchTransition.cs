namespace Machina.Runtime.Dispatch;

internal interface IDispatchTransition<TState>
{
    bool Matches(string eventName);

    TState Apply(TState state);
}

internal sealed class SetTransition<TState, TValue> : IDispatchTransition<TState>
{
    private readonly string eventName;
    private readonly Func<TState, TValue> get;
    private readonly Func<TState, TValue, TState> set;
    private readonly TValue value;

    public SetTransition(
        string eventName,
        Func<TState, TValue> get,
        Func<TState, TValue, TState> set,
        TValue value)
    {
        this.eventName = eventName;
        this.get = get;
        this.set = set;
        this.value = value;
    }

    public bool Matches(string candidateEventName)
    {
        return string.Equals(eventName, candidateEventName, StringComparison.Ordinal);
    }

    public TState Apply(TState state)
    {
        var current = get(state);
        if (EqualityComparer<TValue>.Default.Equals(current, value))
        {
            return state;
        }

        return set(state, value);
    }
}

internal sealed class ToggleTransition<TState> : IDispatchTransition<TState>
{
    private readonly string eventName;
    private readonly Func<TState, bool> get;
    private readonly Func<TState, bool, TState> set;

    public ToggleTransition(
        string eventName,
        Func<TState, bool> get,
        Func<TState, bool, TState> set)
    {
        this.eventName = eventName;
        this.get = get;
        this.set = set;
    }

    public bool Matches(string candidateEventName)
    {
        return string.Equals(eventName, candidateEventName, StringComparison.Ordinal);
    }

    public TState Apply(TState state)
    {
        var current = get(state);
        return set(state, !current);
    }
}

internal sealed class IncrementTransition<TState> : IDispatchTransition<TState>
{
    private readonly string eventName;
    private readonly Func<TState, int> get;
    private readonly Func<TState, int, TState> set;
    private readonly int by;

    public IncrementTransition(
        string eventName,
        Func<TState, int> get,
        Func<TState, int, TState> set,
        int by)
    {
        this.eventName = eventName;
        this.get = get;
        this.set = set;
        this.by = by;
    }

    public bool Matches(string candidateEventName)
    {
        return string.Equals(eventName, candidateEventName, StringComparison.Ordinal);
    }

    public TState Apply(TState state)
    {
        if (by == 0)
        {
            return state;
        }

        var current = get(state);
        try
        {
            var next = checked(current + by);
            return set(state, next);
        }
        catch (OverflowException)
        {
            throw new MachinaDispatchError(
                code: "InvalidDispatchValue",
                message: $"Increment overflow for event '{eventName}' with value {current} and step {by}.");
        }
    }
}
