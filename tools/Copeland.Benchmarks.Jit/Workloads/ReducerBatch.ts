record CounterState {
    count: int;
    enabled: boolean;
}

enum CounterEvent {
    Increment,
    Reset,
    Toggle,
}

function Reduce(state: CounterState, event: CounterEvent): CounterState {
    return match event {
        Increment => state with { count: state.count + 1 },
        Reset => state with { count: 0 },
        Toggle => state with { enabled: !state.enabled },
    };
}

function Run(iterations: int): int {
    let state: CounterState = { count: 0, enabled: true };

    for (let index: int = 0; index < iterations; index = index + 1) {
        let event: CounterEvent = CounterEvent.Increment;
        if ((index % 37) == 0) {
            event = CounterEvent.Reset;
        }
        else {
            if ((index % 11) == 0) {
                event = CounterEvent.Toggle;
            }
        }

        state = Reduce(state, event);
    }

    if (state.enabled) {
        return state.count + 1000000;
    }

    return state.count;
}
