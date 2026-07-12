namespace Machina.Runtime.Dispatch;
using Machina.Core.Actions;

public interface IDispatchTransition<TState>
{
    UiActionId Action { get; }

    TState Apply(TState state);
}

internal sealed class SetTransition<TState, TValue> : IDispatchTransition<TState>
{
    private readonly Func<TState, TValue> get;
    private readonly Func<TState, TValue, TState> set;
    private readonly TValue value;

    public SetTransition(
        UiActionId action,
        Func<TState, TValue> get,
        Func<TState, TValue, TState> set,
        TValue value)
    {
        Action = action;
        this.get = get;
        this.set = set;
        this.value = value;
    }

    public UiActionId Action { get; }

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
    private readonly Func<TState, bool> get;
    private readonly Func<TState, bool, TState> set;

    public ToggleTransition(
        UiActionId action,
        Func<TState, bool> get,
        Func<TState, bool, TState> set)
    {
        Action = action;
        this.get = get;
        this.set = set;
    }

    public UiActionId Action { get; }

    public TState Apply(TState state)
    {
        var current = get(state);
        return set(state, !current);
    }
}

internal sealed class IncrementTransition<TState> : IDispatchTransition<TState>
{
    private readonly Func<TState, int> get;
    private readonly Func<TState, int, TState> set;
    private readonly int by;

    public IncrementTransition(
        UiActionId action,
        Func<TState, int> get,
        Func<TState, int, TState> set,
        int by)
    {
        Action = action;
        this.get = get;
        this.set = set;
        this.by = by;
    }

    public UiActionId Action { get; }

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
                message: $"Increment overflow for event '{Action.Value}' with value {current} and step {by}.");
        }
    }
}
