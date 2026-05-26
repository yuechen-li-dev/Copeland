namespace Machina.Runtime.Dispatch;
using Machina.Core.Actions;

public static class DispatchTable
{
    public static DispatchTable<TState> For<TState>()
    {
        return DispatchTable<TState>.Empty;
    }

    public static DispatchTable<TState> Create<TState>(IEnumerable<IDispatchTransition<TState>> transitions)
    {
        return DispatchTable<TState>.Create(transitions);
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

    public static DispatchTable<TState> Create(IEnumerable<IDispatchTransition<TState>> transitions)
    {
        if (transitions is null)
        {
            throw new MachinaDispatchError("InvalidDispatchTransition", "Transition list must be provided.");
        }

        var rows = new List<IDispatchTransition<TState>>();
        foreach (var transition in transitions)
        {
            if (transition is null)
            {
                throw new MachinaDispatchError("InvalidDispatchTransition", "Transition entry cannot be null.");
            }

            rows.Add(transition);
        }

        return new DispatchTable<TState>(rows);
    }

    public DispatchTable<TState> Set<TValue>(
        UiActionId action,
        Func<TState, TValue> get,
        Func<TState, TValue, TState> set,
        TValue value)
    {
        ValidateTransitionInputs(action, get, set);
        return Append(new SetTransition<TState, TValue>(action, get, set, value));
    }

    public DispatchTable<TState> Set<TValue>(
        string eventName,
        Func<TState, TValue> get,
        Func<TState, TValue, TState> set,
        TValue value)
    {
        try
        {
            return Set(new UiActionId(eventName), get, set, value);
        }
        catch (ArgumentException)
        {
            throw new MachinaDispatchError("InvalidDispatchTransition", "Transition event name must be non-empty.");
        }
    }

    public DispatchTable<TState> Toggle(
        UiActionId action,
        Func<TState, bool> get,
        Func<TState, bool, TState> set)
    {
        ValidateTransitionInputs(action, get, set);
        return Append(new ToggleTransition<TState>(action, get, set));
    }

    public DispatchTable<TState> Toggle(
        string eventName,
        Func<TState, bool> get,
        Func<TState, bool, TState> set)
    {
        try
        {
            return Toggle(new UiActionId(eventName), get, set);
        }
        catch (ArgumentException)
        {
            throw new MachinaDispatchError("InvalidDispatchTransition", "Transition event name must be non-empty.");
        }
    }

    public DispatchTable<TState> Increment(
        UiActionId action,
        Func<TState, int> get,
        Func<TState, int, TState> set,
        int by = 1)
    {
        ValidateTransitionInputs(action, get, set);
        return Append(new IncrementTransition<TState>(action, get, set, by));
    }

    public DispatchTable<TState> Increment(
        string eventName,
        Func<TState, int> get,
        Func<TState, int, TState> set,
        int by = 1)
    {
        try
        {
            return Increment(new UiActionId(eventName), get, set, by);
        }
        catch (ArgumentException)
        {
            throw new MachinaDispatchError("InvalidDispatchTransition", "Transition event name must be non-empty.");
        }
    }

    public TState Dispatch(TState state, UiActionId action)
    {
        return Dispatch(state, action.Value);
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
            if (transition.Action.Value == eventName)
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
        UiActionId action,
        Func<TState, TValue> get,
        Func<TState, TValue, TState> set)
    {
        if (get is null || set is null)
        {
            throw new MachinaDispatchError("InvalidDispatchTransition", "Transition getter and setter must be provided.");
        }
    }
}
