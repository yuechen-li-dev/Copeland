export record AppState {
    count: int;
    serialized: string;
    loading: boolean;
    error: string;
}

export enum AppEvent {
    Increment,
    SerializationCompleted(serialized: string),
    SerializationFailed(message: string),
}

export function InitialState(): AppState {
    return { count: 0, serialized: "", loading: true, error: "" };
}

export function Reduce(state: AppState, event: AppEvent): AppState {
    return switch event {
        Increment => state with { count: state.count + 1, loading: true, error: "" },
        SerializationCompleted(serialized) => state with { serialized: serialized, loading: false, error: "" },
        SerializationFailed(message) => state with { loading: false, error: message },
    };
}
