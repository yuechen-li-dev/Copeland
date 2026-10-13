import { createElement } from "react";
import { AppEvent, AppState } from "./State";
import { Issue, IssueStatus, VisibleIssues, SelectedIssue, IssueIdentity, IssueLabel } from "./Issues";
import { ShortCopy, LongCopy, IssueDescription } from "./Content";
import { Arrangement, TwoPane } from "./Layouts";
import { Action, NoticeKind, PresentNotice, PresentChoices, PresentReading, PresentHeader, PageSurface } from "./UI";

layout IssueWideLayout = instantiate TwoPane<sidebarWidth: 300px>;
layout IssueStackedLayout = instantiate TwoPane<arrangement: Arrangement.Vertical>;

function SelectIssue(issue: Issue): AppEvent {
    return AppEvent.SelectIssue(issue.id);
}

function EditSearch(value: string): AppEvent {
    return AppEvent.SetQueryDraft(value);
}

function IssueChoices(state: AppState, send: (event: AppEvent) => void): ReactNode {
    const selected: Option<string> = match state.selected {
        Some(id) => Some(String.From(id)),
        None => None,
    };
    return PresentChoices<Issue, AppEvent>({
        label: "Issue list",
        heading: "Incoming issues",
        items: VisibleIssues(state.rows, state.query),
        identity: IssueIdentity,
        text: IssueLabel,
        select: SelectIssue,
        selected,
        emptyMessage: "No matching issues.",
    }, send);
}

function IssueList(state: AppState, send: (event: AppEvent) => void): ReactNode {
    return match state.status {
        Loading => PresentNotice<AppEvent>({
            kind: NoticeKind.Pending,
            message: "Loading issues…",
            action: None,
        }, send),
        Failed(message) => PresentNotice<AppEvent>({
            kind: NoticeKind.Failure,
            message,
            action: Some(Action<AppEvent>("Retry", AppEvent.SetStatus(IssueStatus.Ready))),
        }, send),
        Ready => IssueChoices(state, send),
    };
}

function IssueDetail(state: AppState, send: (event: AppEvent) => void): ReactNode {
    const issue: Option<Issue> = SelectedIssue(state.rows, state.selected);
    return match issue {
        None => PresentNotice<AppEvent>({
            kind: NoticeKind.Information,
            message: "Select an issue to read its details.",
            action: None,
        }, send),
        Some(selected) => PresentReading({
            id: "issue-detail",
            eyebrow: "SELECTED ISSUE " + String.From(selected.id),
            title: selected.title,
            summary: IssueDescription(),
            prose: if state.longCopy { LongCopy() } else { ShortCopy() },
            proseRegion: "detail",
            linkLabel: "Return to navigation",
            linkTarget: "#app",
        }),
    };
}

function IssueBody(state: AppState, send: (event: AppEvent) => void): ReactNode {
    stream IssueContent {
        list: IssueList(state, send);
        detail: IssueDetail(state, send);
    }
    if (state.stacked || state.narrow) {
        return bind IssueStackedLayout using IssueContent;
    }
    return bind IssueWideLayout using IssueContent;
}

export function IssuesPage(state: AppState, send: (event: AppEvent) => void): ReactNode {
    const header: ReactNode = PresentHeader<AppEvent>({
        title: "Issue triage",
        search: { id: "search", label: "Search issue titles", value: state.queryDraft, edit: EditSearch },
        actions: [
            Action<AppEvent>("Search", AppEvent.Search(state.queryDraft)),
            Action<AppEvent>("Insert issue", AppEvent.InsertIssue),
            Action<AppEvent>("Remove selected issue", AppEvent.RemoveSelectedIssue),
            Action<AppEvent>("Reverse issues", AppEvent.ReverseIssues),
            Action<AppEvent>("Change arrangement", AppEvent.ToggleArrangement),
            Action<AppEvent>("Show loading", AppEvent.SetStatus(IssueStatus.Loading)),
            Action<AppEvent>("Show error", AppEvent.SetStatus(IssueStatus.Failed("Could not load issues."))),
            Action<AppEvent>("Show ready", AppEvent.SetStatus(IssueStatus.Ready)),
        ],
    }, send);
    return PageSurface([header, IssueBody(state, send)]);
}
