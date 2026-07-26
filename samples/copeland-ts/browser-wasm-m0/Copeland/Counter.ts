export record CounterState {
    count: int;
}

export enum CounterEvent {
    Increment,
    Reset,
}

export function Reduce(state: CounterState, event: CounterEvent): CounterState {
    return switch event {
        Increment => state with { count: state.count + 1 },
        Reset => state with { count: 0 },
    };
}

export function ApplyIncrement(count: int): int {
    const next: CounterState = Reduce({ count: count }, CounterEvent.Increment);
    return next.count;
}

export function ApplyReset(count: int): int {
    const next: CounterState = Reduce({ count: count }, CounterEvent.Reset);
    return next.count;
}
