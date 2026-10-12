import { useReducer, useState } from "react";
import { createRoot } from "react-dom/client";
import { Dialog } from "@base-ui-components/react/dialog";
import { ArticleTitle, ShortCopy, LongCopy, LongToken, IssueOne, IssueTwo, IssueThree, IssueExtra, IssueDescription, ValidationMessage } from "../src/Content";

type State = {
    page: number;
    longCopy: boolean;
    inserted: boolean;
    stacked: boolean;
    query: string;
    selected: number;
    status: number;
    name: string;
    validation: boolean;
    saved: boolean;
    dialogOpen: boolean;
};

type Event =
    | { kind: "navigate"; page: number }
    | { kind: "copy" }
    | { kind: "insert" }
    | { kind: "arrange" }
    | { kind: "search"; query: string }
    | { kind: "select"; selected: number }
    | { kind: "status"; status: number }
    | { kind: "save"; name: string }
    | { kind: "dialog"; open: boolean };

const initialState: State = {
    page: 0, longCopy: false, inserted: false, stacked: false,
    query: "", selected: 1, status: 0, name: "",
    validation: false, saved: false, dialogOpen: false
};

function reduce(state: State, event: Event): State {
    switch (event.kind) {
        case "navigate": return { ...state, page: event.page };
        case "copy": return { ...state, longCopy: !state.longCopy };
        case "insert": return { ...state, inserted: true };
        case "arrange": return { ...state, stacked: !state.stacked };
        case "search": return { ...state, query: event.query };
        case "select": return { ...state, selected: event.selected };
        case "status": return { ...state, status: event.status };
        case "save": return { ...state, name: event.name, validation: event.name === "", saved: event.name !== "" };
        case "dialog": return { ...state, dialogOpen: event.open };
    }
}

type Props = { state: State; send: (event: Event) => void };

function ArticleHeading() {
    return <header className="panel">
        <p className="eyebrow">FIELD NOTES / 07</p>
        <h1>{ArticleTitle()}</h1>
        <p>Content, arrangement, and behavior under revision.</p>
    </header>;
}

function Prose({ state, region }: { state: State; region: string }) {
    return <p className="readable" data-prose={region}>{state.longCopy ? LongCopy() : ShortCopy()}</p>;
}

function ArticleBody({ state }: { state: State }) {
    return <article className="panel reading" id="article-body">
        <h2>Keep the explanation intact</h2>
        <Prose state={state} region="article" />
        <ul><li>Keep meaningful headings.</li><li>Preserve links and selection.</li><li>Make deliberate overflow visible.</li></ul>
        <h2>Identifiers and code</h2>
        <p className="token">{LongToken()}</p>
        <pre tabIndex={0} aria-label="Scrollable code"><code>{LongToken()}</code></pre>
        <p><a href="#article-notes">Read the supporting notes</a></p>
    </article>;
}

function Notes() {
    return <aside className="panel" id="article-notes"><h2>Supporting notes</h2>
        <p className="readable">The same paragraph component is reused in the detail pane and the overflow specimen.</p>
    </aside>;
}

function issueData(inserted: boolean) {
    const issues = [
        { id: 1, title: IssueOne() },
        { id: 2, title: IssueTwo() },
        { id: 3, title: IssueThree() }
    ];
    if (inserted) {
        issues.push({ id: 4, title: IssueExtra() });
    }
    return issues;
}

function IssueList({ state, send }: Props) {
    if (state.status === 1) {
        return <section className="panel" role="status">Loading issues…</section>;
    }
    if (state.status === 2) {
        return <section className="panel" role="alert">Could not load issues. <button onClick={() => send({ kind: "status", status: 0 })}>Retry</button></section>;
    }
    const issues = issueData(state.inserted).filter(issue => issue.title.includes(state.query));
    if (issues.length === 0) {
        return <section className="panel" role="status">No matching issues.</section>;
    }
    return <section className="panel" aria-label="Issue list"><h2>Incoming issues</h2>
        <ul className="issue-list">{issues.map(issue =>
            <li key={issue.id}><button data-issue={issue.id} onClick={() => send({ kind: "select", selected: issue.id })}>{issue.title}</button></li>
        )}</ul>
    </section>;
}

function IssueDetail({ state }: { state: State }) {
    const selected = issueData(true).find(issue => issue.id === state.selected)!;
    return <article className="panel reading" id="issue-detail">
        <p className="eyebrow">SELECTED ISSUE {state.selected}</p>
        <h2>{selected.title}</h2>
        <p className="readable">{IssueDescription()}</p>
        <Prose state={state} region="detail" />
        <a href="#app">Return to navigation</a>
    </article>;
}

function IssuesPage({ state, send }: Props) {
    const [draft, setDraft] = useState("");
    return <section>
        <header className="panel"><h1>Issue triage</h1>
            <label htmlFor="search">Search issue titles</label>
            <input id="search" type="search" value={draft} onChange={event => setDraft(event.currentTarget.value)} />
            <button onClick={() => send({ kind: "search", query: draft })}>Search</button>
            <button onClick={() => send({ kind: "insert" })}>Insert issue</button>
            <button onClick={() => send({ kind: "arrange" })}>Change arrangement</button>
            <button onClick={() => send({ kind: "status", status: 1 })}>Show loading</button>
            <button onClick={() => send({ kind: "status", status: 2 })}>Show error</button>
            <button onClick={() => send({ kind: "status", status: 0 })}>Show ready</button>
        </header>
        <div className={state.stacked ? "baseline-issues stacked" : "baseline-issues"}>
            <IssueList state={state} send={send} />
            <IssueDetail state={state} />
        </div>
    </section>;
}

function SettingsPage({ state, send }: Props) {
    const [draft, setDraft] = useState("");
    return <section><h1>Workspace settings</h1>
        <div className="baseline-column">
            <section className="panel reading"><h2>Workspace identity</h2>
                <label htmlFor="workspace-name">Workspace name</label>
                <input id="workspace-name" type="text" value={draft} onChange={event => setDraft(event.currentTarget.value)} />
                <button onClick={() => send({ kind: "save", name: draft })}>Save workspace</button>
                {state.validation && <p role="alert" className="readable">{ValidationMessage()}</p>}
                {state.saved && <p role="status">Saved workspace: {state.name}</p>}
                <button onClick={() => send({ kind: "dialog", open: true })}>Review settings</button>
            </section>
            <aside className="panel"><h2>Before saving</h2><p className="readable">Use a name your collaborators can recognize. Validation should stay beside the control it explains.</p></aside>
        </div>
        <Dialog.Root open={state.dialogOpen} onOpenChange={open => send({ kind: "dialog", open })}>
            <Dialog.Portal><Dialog.Backdrop className="dialog-backdrop" />
                <Dialog.Popup className="dialog-popup"><Dialog.Title>Review workspace settings</Dialog.Title>
                    <Dialog.Description>Confirm that the name and validation remain readable.</Dialog.Description>
                    <Dialog.Close>Close review</Dialog.Close>
                </Dialog.Popup>
            </Dialog.Portal>
        </Dialog.Root>
    </section>;
}

function Page({ state, send }: Props) {
    if (state.page === 1) { return <IssuesPage state={state} send={send} />; }
    if (state.page === 2) { return <SettingsPage state={state} send={send} />; }
    if (state.page === 3) {
        return <section><h1>Assigned-height overflow specimen</h1><p>The prose slot is deliberately 180px tall. Compare its content with the following region.</p>
            <div className="baseline-bounded"><ArticleBody state={state} /></div><Notes />
        </section>;
    }
    return <div className="baseline-column article-flow"><ArticleHeading /><ArticleBody state={state} /><Notes /></div>;
}

function App() {
    const [state, send] = useReducer(reduce, initialState);
    return <main className="shell"><nav className="toolbar" aria-label="Specimens">
        <strong>Ordinary React + TS</strong>
        <button onClick={() => send({ kind: "navigate", page: 0 })}>Article</button>
        <button onClick={() => send({ kind: "navigate", page: 1 })}>Issues</button>
        <button onClick={() => send({ kind: "navigate", page: 2 })}>Settings</button>
        <button onClick={() => send({ kind: "navigate", page: 3 })}>Overflow</button>
        <button onClick={() => send({ kind: "copy" })}>Change copy length</button>
    </nav><Page state={state} send={send} /></main>;
}

createRoot(document.getElementById("app")!).render(<App />);
