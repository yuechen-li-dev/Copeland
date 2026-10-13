import { SearchField, Panel } from "@copeland/presentation-controls";
import { ownChild } from "@copeland/browser-v1";
import { search, saveDraft, focus } from "@copeland/lifecycle-host";
import { Map } from "./Collections";

const $schema: string = "copeland://presentation/lifecycle-demo/v1";

record Row { id: string; title: string; }
record DirectoryState { request: int; query: string; count: int; reversed: boolean; }
record EditorState { draft: string; status: SaveStatus; }
enum SaveStatus { Ready, Saving, Saved(draft: string), Failed(message: string) }

async function Search(query: string): string ! string {
    return await search(query);
}

async function Persist(id: string, draft: string): string ! string {
    return await saveDraft(id, draft);
}

function Focus(id: string): void { focus(id); }

function AcceptSearch(state: DirectoryState, request: int, response: string ! string): DirectoryState {
    if (request != state.request) { return state; }
    return match response {
        ok(answer) => state with { count: SearchCount(answer) },
        err(message) => state with { count: 0 },
    };
}

function AcceptSave(state: EditorState, response: string ! string): EditorState {
    return state with { status: match response {
        ok(draft) => SaveStatus.Saved(draft),
        err(message) => SaveStatus.Failed(message),
    } };
}

function SaveText(status: SaveStatus): string {
    return match status {
        Ready => "Ready",
        Saving => "Saving",
        Saved(draft) => "Saved: " + draft,
        Failed(message) => "Failed: " + message,
    };
}

export function Editor(id: string, title: string): View {
    state: EditorState = { draft: title, status: SaveStatus.Ready };
    on Edit(draft: string) => state with { draft };
    on Saved(response: string ! string) => AcceptSave(state, response);
    on Save(id: string) => state with { status: SaveStatus.Saving }
        effect Persist(id, state.draft) => Saved(result);
    return <section aria-label={id}>
        <h2>{title}</h2>
        <label for={id}>Draft</label>
        <SearchField id={id} value={state.draft} onValueChange={on.Edit} />
        <button onClick={capture { id, send: on.Save } () => send(id)}>Save</button>
        <p>{SaveText(state.status)}</p>
    </section>;
}

function Rows(count: int, reversed: boolean): Row[] {
    const first: Row = { id: "amber", title: "Amber draft" };
    const second: Row = { id: "blue", title: "Blue draft" };
    if (count == 0) { return []; }
    if (count == 1) { return [first]; }
    if (reversed) { return [second, first]; }
    return [first, second];
}

function PresentRow(row: Row): View {
    return ownChild("editors", row.id, Editor(row.id, row.title));
}

export function Directory(): View {
    state: DirectoryState = { request: 0, query: "", count: 2, reversed: false };
    on Found(request: int, response: string ! string) => AcceptSearch(state, request, response);
    on Search(query: string) => state with { request: state.request + 1, query }
        effect Search(query) => Found(state.request + 1, result);
    on Reverse() => state with { reversed: !state.reversed };
    on Remove() => state with { count: 1 };
    on Restore() => state with { count: 2 };
    on Focus(id: string) => state
        after PresentationCommitted effect Focus(id);
    const rows: View[] = Map<Row, View>(Rows(state.count, state.reversed), PresentRow);
    return <main>
        <h1>Owned editable search results</h1>
        <label for="directory-query">Search: all, one, or none</label>
        <SearchField id="directory-query" value={state.query} onValueChange={on.Search} />
        <button onClick={on.Reverse}>Reverse</button>
        <button onClick={on.Remove}>Remove blue</button>
        <button onClick={on.Restore}>Restore blue</button>
        <button onClick={capture { send: on.Focus } () => send("amber")}>Focus amber</button>
        <button onClick={capture { send: on.Focus } () => send("blue")}>Focus blue</button>
        <Panel label="Editors">{rows}</Panel>
    </main>;
}

function SearchCount(answer: string): int {
    if (answer == "none") { return 0; }
    if (answer == "one") { return 1; }
    return 2;
}

