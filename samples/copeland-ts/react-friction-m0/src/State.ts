import { Issue, IssueStatus, InitialIssues, InsertIssue, RemoveIssue, ReverseIssues } from "./Issues";

export enum Page {
    Article,
    Issues,
    Settings,
    Overflow,
}

export record AppState {
    page: Page;
    longCopy: boolean;
    rows: Issue[];
    nextIssueId: int;
    stacked: boolean;
    narrow: boolean;
    query: string;
    queryDraft: string;
    nameDraft: string;
    selected: Option<int>;
    status: IssueStatus;
    name: string;
    validation: boolean;
    saved: boolean;
    dialogOpen: boolean;
}

export enum AppEvent {
    Navigate(page: Page),
    ToggleCopy,
    InsertIssue,
    RemoveSelectedIssue,
    ReverseIssues,
    ToggleArrangement,
    Resize(width: int),
    Search(query: string),
    SetQueryDraft(value: string),
    SetNameDraft(value: string),
    SelectIssue(selected: int),
    SetStatus(status: IssueStatus),
    Save(name: string),
    SetDialog(open: boolean),
}

export function InitialState(width: int): AppState {
    return {
        page: Page.Article,
        longCopy: false,
        rows: InitialIssues(),
        nextIssueId: 4,
        stacked: false,
        narrow: width < 600,
        query: "",
        queryDraft: "",
        nameDraft: "",
        selected: Some(1),
        status: IssueStatus.Ready,
        name: "",
        validation: false,
        saved: false,
        dialogOpen: false,
    };
}

export function Reduce(state: AppState, event: AppEvent): AppState {
    return switch event {
        Navigate(page) => state with { page },
        ToggleCopy => state with { longCopy: !state.longCopy },
        InsertIssue => state with {
            rows: InsertIssue(state.rows, state.nextIssueId),
            nextIssueId: state.nextIssueId + 1,
        },
        RemoveSelectedIssue => state with {
            rows: RemoveIssue(state.rows, state.selected),
            selected: None,
        },
        ReverseIssues => state with { rows: ReverseIssues(state.rows) },
        ToggleArrangement => state with { stacked: !state.stacked },
        Resize(width) => state with { narrow: width < 600 },
        Search(query) => state with { query },
        SetQueryDraft(value) => state with { queryDraft: value },
        SetNameDraft(value) => state with { nameDraft: value },
        SelectIssue(selected) => state with { selected: Some(selected) },
        SetStatus(status) => state with { status },
        Save(name) => state with { name, validation: name == "", saved: name != "" },
        SetDialog(open) => state with { dialogOpen: open },
    };
}
