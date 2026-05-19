namespace Machina.Runtime.Dispatch;

public static class DispatchTable
{
    public static DispatchTable<TState> For<TState>()
    {
        return DispatchTable<TState>.Empty;
    }
}

public sealed class DispatchTable<TState>
{
    private readonly IReadOnlyList<IDispatchTransition<TState>> transitions;

    internal static DispatchTable<TState> Empty { get; } = new([]);

    private DispatchTable(IReadOnlyList<IDispatchTransition<TState>> transitions)
    {
        this.transitions = transitions;
    }

    public DispatchTable<TState> Set<TValue>(
        string eventName,
        Func<TState, TValue> get,
        Func<TState, TValue, TState> set,
        TValue value)
    {
        ValidateTransitionInputs(eventName, get, set);
        return Append(new SetTransition<TState, TValue>(eventName, get, set, value));
    }

    public DispatchTable<TState> Toggle(
        string eventName,
        Func<TState, bool> get,
        Func<TState, bool, TState> set)
    {
        ValidateTransitionInputs(eventName, get, set);
        return Append(new ToggleTransition<TState>(eventName, get, set));
    }

    public DispatchTable<TState> Increment(
        string eventName,
        Func<TState, int> get,
        Func<TState, int, TState> set,
        int by = 1)
    {
        ValidateTransitionInputs(eventName, get, set);
        return Append(new IncrementTransition<TState>(eventName, get, set, by));
    }

    public TState Dispatch(TState state, string eventName)
    {
        if (state is null)
        {
            throw new MachinaDispatchError("InvalidDispatchValue", "Dispatch state cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(eventName))
        {
            throw new MachinaDispatchError("InvalidDispatchEvent", "Dispatch event name must be non-empty.");
        }

        foreach (var transition in transitions)
        {
            if (transition.Matches(eventName))
            {
                return transition.Apply(state);
            }
        }

        return state;
    }

    private DispatchTable<TState> Append(IDispatchTransition<TState> transition)
    {
        var next = new List<IDispatchTransition<TState>>(transitions.Count + 1);
        next.AddRange(transitions);
        next.Add(transition);
        return new DispatchTable<TState>(next);
    }

    private static void ValidateTransitionInputs<TValue>(
        string eventName,
        Func<TState, TValue> get,
        Func<TState, TValue, TState> set)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            throw new MachinaDispatchError("InvalidDispatchTransition", "Transition event name must be non-empty.");
        }

        if (get is null || set is null)
        {
            throw new MachinaDispatchError("InvalidDispatchTransition", "Transition getter and setter must be provided.");
        }
    }
}
