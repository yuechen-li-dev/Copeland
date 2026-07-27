export record AppState {
    count: int;
}

export enum AppEvent {
    Increment,
}

export function InitialState(): AppState {
    return { count: 0 };
}

export function Reduce(state: AppState, event: AppEvent): AppState {
    return switch event {
        Increment => state with { count: state.count + 1 },
    };
}
