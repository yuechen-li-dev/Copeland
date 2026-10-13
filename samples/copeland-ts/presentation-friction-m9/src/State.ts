import { Issue, IssueStatus, InitialIssues, InsertIssue, RemoveIssue, ReverseIssues } from "./Issues";
import type { IssueSelection } from "./Issues";

export record AppState {
    longCopy: boolean;
    rows: Issue[];
    nextIssueId: int;
    stacked: boolean;
    narrow: boolean;
    query: string;
    queryDraft: string;
    selected: IssueSelection;
    status: IssueStatus;
}

export enum AppEvent {
    ToggleCopy,
    InsertIssue,
    RemoveSelectedIssue,
    ReverseIssues,
    ToggleArrangement,
    Resize(width: int),
    Search(query: string),
    SetQueryDraft(value: string),
    SelectIssue(selected: int),
    SetStatus(status: IssueStatus),
}

export function InitialState(width: int): AppState {
    return {
        longCopy: false,
        rows: InitialIssues(),
        nextIssueId: 4,
        stacked: false,
        narrow: width < 600,
        query: "",
        queryDraft: "",
        selected: Some(1),
        status: IssueStatus.Ready,
    };
}

export function Reduce(state: AppState, event: AppEvent): AppState {
    return match event {
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
        SelectIssue(selected) => state with { selected: Some(selected) },
        SetStatus(status) => state with { status },
    };
}
