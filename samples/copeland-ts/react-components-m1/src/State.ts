export record AppState {
    dialogOpen: boolean;
}

export enum AppEvent {
    OpenDialog,
    CloseDialog,
}

export function InitialState(): AppState {
    return { dialogOpen: false };
}

export function Reduce(state: AppState, event: AppEvent): AppState {
    return switch event {
        OpenDialog => state with { dialogOpen: true },
        CloseDialog => state with { dialogOpen: false },
    };
}
