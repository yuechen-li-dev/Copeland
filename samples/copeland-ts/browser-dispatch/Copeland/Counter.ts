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

export function SendIncrement(send: (event: CounterEvent) => void): void {
    send(CounterEvent.Increment);
}

export function SendReset(send: (event: CounterEvent) => void): void {
    send(CounterEvent.Reset);
}
