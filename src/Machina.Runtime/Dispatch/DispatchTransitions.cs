using Machina.Core.Actions;

namespace Machina.Runtime.Dispatch;

public static class DispatchTransitions
{
    public static IDispatchTransition<TState> Set<TState, TValue>(
        UiActionId action,
        Func<TState, TValue> get,
        Func<TState, TValue, TState> set,
        TValue value)
    {
        return new SetTransition<TState, TValue>(action, get, set, value);
    }

    public static IDispatchTransition<TState> Toggle<TState>(
        UiActionId action,
        Func<TState, bool> get,
        Func<TState, bool, TState> set)
    {
        return new ToggleTransition<TState>(action, get, set);
    }

    public static IDispatchTransition<TState> Increment<TState>(
        UiActionId action,
        Func<TState, int> get,
        Func<TState, int, TState> set,
        int by = 1)
    {
        return new IncrementTransition<TState>(action, get, set, by);
    }
}
