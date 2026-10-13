import {
    defineComponent,
    h,
    onBeforeUnmount,
    onMounted,
    onUpdated,
    ref,
    useId,
    type PropType,
} from "vue";
import { connectReact, connectVue } from "./executors.ts";
import type { PresentationTicket } from "../../../../src/Copeland/Copeland.TS.Backend.JavaScript/Browser/PresentationIsland.ts";

function presentationHost(connect: typeof connectReact) {
    return defineComponent({
        props: {
            hostKey: { type: String, required: true },
            id: { type: String, required: true },
            label: { type: String, required: true },
            value: { type: String, required: true },
            onValueChange: {
                type: Function as PropType<(value: string) => void>,
                required: true,
            },
            __cope_presentation: {
                type: Object as PropType<PresentationTicket>,
                required: true,
            },
        },
        setup(props) {
            const id = "island-" + useId();
            const element = ref<HTMLElement>();
            let connection: ReturnType<typeof connect> | undefined;
            function update() {
                connection?.update(props.__cope_presentation, {
                    id: props.id,
                    label: props.label,
                    value: props.value,
                    onValueChange: props.onValueChange,
                });
            }
            onMounted(() => {
                if (element.value === undefined)
                    throw new Error("Missing assigned Vue host.");
                connection = connect(element.value);
                update();
            });
            onUpdated(update);
            onBeforeUnmount(() => connection?.release());
            return () =>
                h("div", {
                    id,
                    ref: element,
                    "data-island-host": props.hostKey,
                });
        },
    });
}

export const ReactEditor = presentationHost(connectReact);
export const VueEditor = presentationHost(connectVue);
