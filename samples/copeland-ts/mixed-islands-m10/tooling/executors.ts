import { createElement, useLayoutEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { Input } from "@base-ui-components/react/input";
import {
    createApp,
    defineComponent,
    h,
    onMounted,
    onUpdated,
    ref,
    shallowRef,
} from "vue";
import { registerIslandExecutor } from "../../../../src/Copeland/Copeland.TS.Backend.JavaScript/Browser/PresentationIsland.ts";

export interface ControlProps {
    id: string;
    label: string;
    value: string;
    onValueChange(value: string): void;
}

interface Presentation {
    props: ControlProps;
    committed(): void;
}

// Diagnostic controls let qualification hold and deliver exact commit tickets.
// They do not mutate Copeland state or replace either framework's commit hook.
export const inspection = {
    held: false,
    commits: [] as { id: string; value: string; commit(): void }[],
    callbacks: [] as { id: string; change(value: string): void }[],
};
Object.defineProperty(globalThis, "__islandInspection", { value: inspection });

function committed(presentation: Presentation) {
    const commit = {
        id: presentation.props.id,
        value: presentation.props.value,
        commit: presentation.committed,
    };
    inspection.commits.push(commit);
    if (!inspection.held) commit.commit();
}

function observed(props: ControlProps): ControlProps {
    inspection.callbacks.push({ id: props.id, change: props.onValueChange });
    return props;
}

function ReactControl(presentation: Presentation) {
    const [local, setLocal] = useState(0);
    useLayoutEffect(() => committed(presentation), [presentation]);
    const props = presentation.props;
    return createElement(
        "section",
        { "data-framework": "react" },
        createElement("h2", null, props.label),
        createElement("label", { htmlFor: props.id }, "React draft"),
        createElement(Input, {
            id: props.id,
            value: props.value,
            onValueChange: props.onValueChange,
        }),
        createElement(
            "button",
            { onClick: () => setLocal(local + 1) },
            "React local: " + local,
        ),
    );
}

export const connectReact = registerIslandExecutor<ControlProps>({
    identity: "copeland.demo.react-control.v1",
    mount(host, props, commit) {
        const root = createRoot(host);
        let released = false;
        const instance = {
            update(next: ControlProps, acknowledged: () => void) {
                if (released) throw new Error("React island root has retired.");
                root.render(
                    createElement(ReactControl, {
                        props: observed(next),
                        committed: acknowledged,
                    }),
                );
            },
            unmount() {
                if (released) return;
                released = true;
                // Foreign roots can retire during an outer framework commit.
                // Retire their connection now and release React at its next safe boundary.
                queueMicrotask(() => root.unmount());
            },
        };
        instance.update(props, commit);
        return instance;
    },
});

export const connectVue = registerIslandExecutor<ControlProps>({
    identity: "copeland.demo.vue-control.v1",
    mount(host, props, commit) {
        const presentation = shallowRef<Presentation>({
            props: observed(props),
            committed: commit,
        });
        const Control = defineComponent({
            setup() {
                const local = ref(0);
                onMounted(() => committed(presentation.value));
                onUpdated(() => committed(presentation.value));
                return () => {
                    const props = presentation.value.props;
                    return h("section", { "data-framework": "vue" }, [
                        h("h2", props.label),
                        h("label", { for: props.id }, "Vue draft"),
                        h("input", {
                            id: props.id,
                            value: props.value,
                            onInput: (event: Event) =>
                                props.onValueChange(
                                    (event.target as HTMLInputElement).value,
                                ),
                        }),
                        h(
                            "button",
                            {
                                onClick: () => {
                                    local.value += 1;
                                },
                            },
                            "Vue local: " + local.value,
                        ),
                    ]);
                };
            },
        });
        const app = createApp(Control);
        app.mount(host);
        let released = false;
        return {
            update(next: ControlProps, acknowledged: () => void) {
                if (released) throw new Error("Vue island root has retired.");
                presentation.value = {
                    props: observed(next),
                    committed: acknowledged,
                };
            },
            unmount() {
                if (released) return;
                released = true;
                app.unmount();
            },
        };
    },
});
