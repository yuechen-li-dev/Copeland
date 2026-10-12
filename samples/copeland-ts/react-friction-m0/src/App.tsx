import { createElement } from "react";
import { Dialog } from "@base-ui-components/react/dialog";
import { Input } from "@base-ui-components/react/input";
import { AppEvent, AppState } from "./State";
import { ArticleTitle, ShortCopy, LongCopy, LongToken, IssueOne, IssueTwo, IssueThree, IssueExtra, IssueDescription, ValidationMessage } from "./Content";

import { Arrangement, Reading, TwoPane } from "./Layouts";

layout ArticleReading = instantiate Reading<> with { root.padding: 16px; };

stream ArticleChrome {
    heading: ArticleHeading();
    notes: Notes();
}

layout IssueWideLayout = instantiate TwoPane<sidebarWidth: 300px>;
layout IssueStackedLayout = instantiate TwoPane<arrangement: Arrangement.Vertical>;

function Copy(state: AppState): string {
    if (state.longCopy) {
        return LongCopy();
    }
    return ShortCopy();
}

function Prose(state: AppState, region: string): ReactNode {
    return <p className="readable" data-prose={region}>{Copy(state)}</p>;
}

function ArticleHeading(): ReactNode {
    return <header className="panel">
        <p className="eyebrow">FIELD NOTES / 07</p>
        <h1>{ArticleTitle()}</h1>
        <p>Content, arrangement, and behavior under revision.</p>
    </header>;
}

function ArticleBody(state: AppState): ReactNode {
    return <article className="panel reading" id="article-body">
        <h2>Keep the explanation intact</h2>
        {Prose(state, "article")}
        <ul><li>Keep meaningful headings.</li><li>Preserve links and selection.</li><li>Make deliberate overflow visible.</li></ul>
        <h2>Identifiers and code</h2>
        <p className="token">{LongToken()}</p>
        <pre tabIndex={0} aria-label="Scrollable code"><code>{LongToken()}</code></pre>
        <p><a href="#article-notes">Read the supporting notes</a></p>
    </article>;
}

function Notes(): ReactNode {
    return <aside className="panel" id="article-notes">
        <h2>Supporting notes</h2>
        <p className="readable">The same paragraph component is reused in the detail pane and the overflow specimen.</p>
    </aside>;
}

function ArticlePage(state: AppState): ReactNode {
    stream ArticleContent {
        heading: ArticleChrome.heading;
        body: ArticleBody(state);
        notes: ArticleChrome.notes;
    }
    return bind ArticleReading using ArticleContent;
}

function IssueRow(state: AppState, send: (event: AppEvent) => void, id: int, title: string): ReactNode {
    if (String.IndexOf(title, state.query) < 0) {
        return <li className="filtered-issue" aria-hidden="true"></li>;
    }
    return <li key={id}><button data-issue={String.From(id)} onClick={capture { send, id } () => send(AppEvent.SelectIssue(id))}>{title}</button></li>;
}

function IssueList(state: AppState, send: (event: AppEvent) => void): ReactNode {
    if (state.status == 1) {
        return <section className="panel" role="status">Loading issues…</section>;
    }
    if (state.status == 2) {
        return <section className="panel" role="alert">Could not load issues. <button onClick={capture { send } () => send(AppEvent.SetStatus(0))}>Retry</button></section>;
    }
    if (String.IndexOf(IssueOne(), state.query) < 0 &&
        String.IndexOf(IssueTwo(), state.query) < 0 &&
        String.IndexOf(IssueThree(), state.query) < 0 &&
        (!state.inserted || String.IndexOf(IssueExtra(), state.query) < 0)) {
        return <section className="panel" role="status">No matching issues.</section>;
    }
    return <section className="panel" aria-label="Issue list">
        <h2>Incoming issues</h2>
        <ul className="issue-list">
            {IssueRow(state, send, 1, IssueOne())}
            {IssueRow(state, send, 2, IssueTwo())}
            {IssueRow(state, send, 3, IssueThree())}
            {if state.inserted { IssueRow(state, send, 4, IssueExtra()) } else { <li className="filtered-issue" aria-hidden="true"></li> }}
        </ul>
    </section>;
}

function SelectedTitle(state: AppState): string {
    if (state.selected == 2) { return IssueTwo(); }
    if (state.selected == 3) { return IssueThree(); }
    if (state.selected == 4) { return IssueExtra(); }
    return IssueOne();
}

function IssueDetail(state: AppState): ReactNode {
    return <article className="panel reading" id="issue-detail">
        <p className="eyebrow">SELECTED ISSUE {state.selected}</p>
        <h2>{SelectedTitle(state)}</h2>
        <p className="readable">{IssueDescription()}</p>
        {Prose(state, "detail")}
        <a href="#app">Return to navigation</a>
    </article>;
}

function IssueBody(state: AppState, send: (event: AppEvent) => void): ReactNode {
    stream IssueContent {
        list: IssueList(state, send);
        detail: IssueDetail(state);
    }
    if (state.stacked || state.narrow) {
        return bind IssueStackedLayout using IssueContent;
    }
    return bind IssueWideLayout using IssueContent;
}

function IssuesPage(state: AppState, send: (event: AppEvent) => void): ReactNode {
    return <section>
        <header className="panel"><h1>Issue triage</h1>
            <label htmlFor="search">Search issue titles</label>
            <Input id="search" type="search" value={state.queryDraft} onValueChange={capture { send } (value: string) => send(AppEvent.SetQueryDraft(value))} />
            <button onClick={capture { send, query: state.queryDraft } () => send(AppEvent.Search(query))}>Search</button>
            <button onClick={capture { send } () => send(AppEvent.InsertIssue)}>Insert issue</button>
            <button onClick={capture { send } () => send(AppEvent.ToggleArrangement)}>Change arrangement</button>
            <button onClick={capture { send } () => send(AppEvent.SetStatus(1))}>Show loading</button>
            <button onClick={capture { send } () => send(AppEvent.SetStatus(2))}>Show error</button>
            <button onClick={capture { send } () => send(AppEvent.SetStatus(0))}>Show ready</button>
        </header>
        {IssueBody(state, send)}
    </section>;
}

function SettingsFields(state: AppState, send: (event: AppEvent) => void): ReactNode {
    return <section className="panel reading">
        <h2>Workspace identity</h2>
        <label htmlFor="workspace-name">Workspace name</label>
        <Input id="workspace-name" type="text" value={state.nameDraft} onValueChange={capture { send } (value: string) => send(AppEvent.SetNameDraft(value))} />
        <button onClick={capture { send, name: state.nameDraft } () => send(AppEvent.Save(name))}>Save workspace</button>
        {if state.validation { <p role="alert" className="readable">{ValidationMessage()}</p> } else { <span></span> }}
        {if state.saved { <p role="status">Saved workspace: {state.name}</p> } else { <span></span> }}
        <button onClick={capture { send } () => send(AppEvent.SetDialog(true))}>Review settings</button>
    </section>;
}

function SettingsHelp(): ReactNode {
    return <aside className="panel"><h2>Before saving</h2><p className="readable">Use a name your collaborators can recognize. Validation should stay beside the control it explains.</p></aside>;
}

function SettingsPage(state: AppState, send: (event: AppEvent) => void): ReactNode {
    stream SettingsSurface<0px, 0px> {
        width: fill;
        height: fit;
        fields: SettingsFields(state, send) { height: fit; }
        help: SettingsHelp() { height: fit; }
    }
    return <section><h1>Workspace settings</h1>
        {SettingsSurface()}
        <Dialog.Root open={state.dialogOpen} onOpenChange={capture { send } (open: boolean) => send(AppEvent.SetDialog(open))}>
            <Dialog.Portal><Dialog.Backdrop className="dialog-backdrop" />
                <Dialog.Popup className="dialog-popup">
                    <Dialog.Title>Review workspace settings</Dialog.Title>
                    <Dialog.Description>Confirm that the name and validation remain readable.</Dialog.Description>
                    <Dialog.Close>Close review</Dialog.Close>
                </Dialog.Popup>
            </Dialog.Portal>
        </Dialog.Root>
    </section>;
}

function OverflowPage(state: AppState): ReactNode {
    stream BoundedSurface<0px, 0px> {
        width: fill;
        height: fit;
        prose: ArticleBody(state) { height: 180px; }
        following: Notes() { height: fit; }
    }
    return <section><h1>Assigned-height overflow specimen</h1><p>The prose slot is deliberately 180px tall. Compare its content with the following region.</p>{BoundedSurface()}</section>;
}

function Page(state: AppState, send: (event: AppEvent) => void): ReactNode {
    if (state.page == 1) { return IssuesPage(state, send); }
    if (state.page == 2) { return SettingsPage(state, send); }
    if (state.page == 3) { return OverflowPage(state); }
    return ArticlePage(state);
}

export function View(state: AppState, send: (event: AppEvent) => void): ReactNode {
    return <main className="shell">
        <nav className="toolbar" aria-label="Specimens">
            <strong>Copeland TS</strong>
            <button onClick={capture { send } () => send(AppEvent.Navigate(0))}>Article</button>
            <button onClick={capture { send } () => send(AppEvent.Navigate(1))}>Issues</button>
            <button onClick={capture { send } () => send(AppEvent.Navigate(2))}>Settings</button>
            <button onClick={capture { send } () => send(AppEvent.Navigate(3))}>Overflow</button>
            <button onClick={capture { send } () => send(AppEvent.ToggleCopy)}>Change copy length</button>
        </nav>
        {Page(state, send)}
    </main>;
}


