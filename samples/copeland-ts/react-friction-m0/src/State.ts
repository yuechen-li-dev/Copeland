export record AppState {
    page: int;
    longCopy: boolean;
    inserted: boolean;
    stacked: boolean;
    narrow: boolean;
    query: string;
    queryDraft: string;
    nameDraft: string;
    selected: int;
    status: int;
    name: string;
    validation: boolean;
    saved: boolean;
    dialogOpen: boolean;
}

export enum AppEvent {
    Navigate(page: int),
    ToggleCopy,
    InsertIssue,
    ToggleArrangement,
    Resize(width: int),
    Search(query: string),
    SetQueryDraft(value: string),
    SetNameDraft(value: string),
    SelectIssue(selected: int),
    SetStatus(status: int),
    Save(name: string),
    SetDialog(open: boolean),
}

export function InitialState(width: int): AppState {
    return {
        page: 0, longCopy: false, inserted: false, stacked: false, narrow: width < 600,
        query: "", queryDraft: "", nameDraft: "", selected: 1, status: 0, name: "",
        validation: false, saved: false, dialogOpen: false
    };
}

export function Reduce(state: AppState, event: AppEvent): AppState {
    return switch event {
        Navigate(page) => state with { page },
        ToggleCopy => state with { longCopy: !state.longCopy },
        InsertIssue => state with { inserted: true },
        ToggleArrangement => state with { stacked: !state.stacked },
        Resize(width) => state with { narrow: width < 600 },
        Search(query) => state with { query },
        SetQueryDraft(value) => state with { queryDraft: value },
        SetNameDraft(value) => state with { nameDraft: value },
        SelectIssue(selected) => state with { selected },
        SetStatus(status) => state with { status },
        Save(name) => state with { name, validation: name == "", saved: name != "" },
        SetDialog(open) => state with { dialogOpen: open },
    };
}
