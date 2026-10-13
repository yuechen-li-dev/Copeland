import { IssueOne, IssueTwo, IssueThree, IssueExtra } from "./Content";
import { Append, Filter, Reverse } from "./Collections";

export type IssueSelection = Option<int>;
export type Titled = { title: string; };

export record Issue {
    id: int;
    title: string;
}

export enum IssueStatus {
    Loading,
    Failed(message: string),
    Ready,
}

export function InitialIssues(): Issue[] {
    return [
        { id: 1, title: IssueOne() },
        { id: 2, title: IssueTwo() },
        { id: 3, title: IssueThree() },
    ];
}

export function InsertIssue(rows: Issue[], id: int): Issue[] {
    let title: string = IssueExtra();
    if (id > 4) {
        title = title + " / " + String.From(id);
    }
    return Append<Issue>(rows, { id, title });
}

export function VisibleIssues(rows: Issue[], query: string): Issue[] {
    return Filter<Issue>(rows,
        capture { query } (issue: Issue) => String.IndexOf(issue.title, query) >= 0);
}

export function RemoveIssue(rows: Issue[], selected: IssueSelection): Issue[] {
    return match selected {
        Some(id) => Filter<Issue>(rows, capture { id } (issue: Issue) => issue.id != id),
        None => rows,
    };
}

export function ReverseIssues(rows: Issue[]): Issue[] {
    return Reverse<Issue>(rows);
}

export function SelectedIssue(rows: Issue[], selected: IssueSelection): Option<Issue> {
    const matches: Issue[] = match selected {
        Some(id) => Filter<Issue>(rows, capture { id } (issue: Issue) => issue.id == id),
        None => [],
    };
    if (matches.length == 0) {
        return None;
    }
    return Some(matches[0]);
}

export function IssueIdentity(issue: Issue): string {
    return String.From(issue.id);
}

export function IssueLabel<T extends Titled>(issue: T): string {
    return issue.title;
}
