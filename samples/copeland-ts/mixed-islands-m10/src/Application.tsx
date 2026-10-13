import { ReactEditor, VueEditor } from "@copeland/island-controls";
import { ownChild } from "@copeland/browser-v1";
import { focus, saveDraft } from "@copeland/lifecycle-host";

const $schema: string = "copeland://mixed-islands/demo/v1";

type SaveResponse = string ! string;
type ChildViews = View[];

enum Framework { React, Vue }
record AppState { reversed: boolean; visible: boolean; heading: string; }
record EditorState { draft: string; status: string; }

layout Surface<0px, 0px> {
    width: fill;
    height: fit;
    column root {
        gap: 12px;
        padding: 24px;
        style: { fill: "#fffefb", radius: 8px,
            text: { size: 18px, color: "#22313c", lineHeight: 1.6, wrap: "anywhere" } };
        slot controls { height: fit; }
    }
}

async function Persist(id: string, draft: string): SaveResponse {
    return await saveDraft(id, draft);
}

function Focus(id: string): void { focus(id); }

function SaveStatus(response: SaveResponse): string {
    return match response {
        ok(draft) => "Saved: " + draft,
        err(message) => "Failed: " + message,
    };
}

function Editor(id: string, framework: Framework, heading: string): View {
    state: EditorState = { draft: id + " draft", status: "Ready" };
    on Edit(draft: string) => state with { draft };
    on Saved(response: SaveResponse) => state with { status: SaveStatus(response) };
    on Save(id: string) => state with { status: "Saving" }
        effect Persist(id, state.draft) => Saved(result);
    on Reveal(id: string) => state with { draft: "Presented " + id }
        after PresentationCommitted effect Focus(id);
    const control: View = match framework {
        React => <ReactEditor hostKey={id} id={id} label={heading + " React"}
            value={state.draft} onValueChange={on.Edit} />,
        Vue => <VueEditor hostKey={id} id={id} label={heading + " Vue"}
            value={state.draft} onValueChange={on.Edit} />,
    };
    stream Content {
        controls: <section aria-label={id}>
            {control}
            <button onClick={capture { id, send: on.Save } () => send(id)}>Save</button>
            <button onClick={capture { id, send: on.Reveal } () => send(id)}>Reveal and focus</button>
            <p>{state.status}</p>
        </section>;
    }
    return bind Surface using Content;
}

function Editors(state: AppState): ChildViews {
    const react: View = ownChild("editors", "react", Editor("react", Framework.React, state.heading));
    if (!state.visible) { return [react]; }
    const vue: View = ownChild("editors", "vue", Editor("vue", Framework.Vue, state.heading));
    if (state.reversed) { return [vue, react]; }
    return [react, vue];
}

function SharedEditor(): View {
    state: string = "Shared draft";
    on Edit(value: string) => value;
    on Reveal() => "Shared presented"
        after PresentationCommitted effect Focus("mirror-vue");
    stream Content {
        controls: <section aria-label="Shared owner">
            <h2>One state owner, two frameworks</h2>
            <ReactEditor hostKey="mirror-react" id="mirror-react" label="Shared React"
                value={state} onValueChange={on.Edit} />
            <VueEditor hostKey="mirror-vue" id="mirror-vue" label="Shared Vue"
                value={state} onValueChange={on.Edit} />
            <button onClick={on.Reveal}>Reveal shared and focus</button>
        </section>;
    }
    return bind Surface using Content;
}

export function Application(): View {
    state: AppState = { reversed: false, visible: true, heading: "Original" };
    on Reverse() => state with { reversed: !state.reversed };
    on Remove() => state with { visible: false };
    on Restore() => state with { visible: true };
    on Rename() => state with { heading: "Updated" };
    return <main>
        <h1>Copeland owns this mixed page</h1>
        <p>React and Vue keep their component internals. Copeland owns drafts, effects, layout, and lifetime.</p>
        <button onClick={on.Reverse}>Reverse islands</button>
        <button onClick={on.Remove}>Remove Vue</button>
        <button onClick={on.Restore}>Restore Vue</button>
        <button onClick={on.Rename}>Update labels</button>
        <section aria-label="Editors">{Editors(state)}</section>
        {ownChild("shared", "mirror", SharedEditor())}
    </main>;
}
