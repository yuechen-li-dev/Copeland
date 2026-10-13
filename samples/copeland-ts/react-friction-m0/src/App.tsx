import { createElement } from "react";
import { Dialog } from "@base-ui-components/react/dialog";
import { Input } from "@base-ui-components/react/input";
import { AppEvent, AppState, Page } from "./State";
import { ArticleTitle, ShortCopy, LongCopy, LongToken, ValidationMessage } from "./Content";

import { Reading } from "./Layouts";
import { IssuesPage } from "./IssuesPage";

layout ArticleReading = instantiate Reading<> with { root.padding: 16px; };

stream ArticleChrome {
    heading: ArticleHeading();
    notes: Notes();
}


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

function CurrentPage(state: AppState, send: (event: AppEvent) => void): ReactNode {
    return match state.page {
        Article => ArticlePage(state),
        Issues => IssuesPage(state, send),
        Settings => SettingsPage(state, send),
        Overflow => OverflowPage(state),
    };
}

export function View(state: AppState, send: (event: AppEvent) => void): ReactNode {
    return <main className="shell">
        <nav className="toolbar" aria-label="Specimens">
            <strong>Copeland TS</strong>
            <button onClick={capture { send } () => send(AppEvent.Navigate(Page.Article))}>Article</button>
            <button onClick={capture { send } () => send(AppEvent.Navigate(Page.Issues))}>Issues</button>
            <button onClick={capture { send } () => send(AppEvent.Navigate(Page.Settings))}>Settings</button>
            <button onClick={capture { send } () => send(AppEvent.Navigate(Page.Overflow))}>Overflow</button>
            <button onClick={capture { send } () => send(AppEvent.ToggleCopy)}>Change copy length</button>
        </nav>
        {CurrentPage(state, send)}
    </main>;
}


