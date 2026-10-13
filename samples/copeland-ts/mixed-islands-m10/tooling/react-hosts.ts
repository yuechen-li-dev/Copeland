import { createElement, useId, useLayoutEffect, useRef } from "react";
import { connectReact, connectVue, type ControlProps } from "./executors.ts";
import type { PresentationTicket } from "../../../../src/Copeland/Copeland.TS.Backend.JavaScript/Browser/PresentationIsland.ts";

type Connect = typeof connectReact;
interface HostProps extends ControlProps {
    hostKey: string;
    __cope_presentation: PresentationTicket;
}

function presentationHost(connect: Connect) {
    return function Host(props: HostProps) {
        const id = "island-" + useId();
        const element = useRef<HTMLDivElement>(null);
        const connection = useRef<ReturnType<Connect> | null>(null);
        useLayoutEffect(() => {
            if (element.current === null)
                throw new Error("Missing assigned React host.");
            connection.current = connect(element.current);
            return () => {
                connection.current?.release();
                connection.current = null;
            };
        }, []);
        useLayoutEffect(() => {
            connection.current?.update(props.__cope_presentation, {
                id: props.id,
                label: props.label,
                value: props.value,
                onValueChange: props.onValueChange,
            });
        });
        return createElement("div", {
            id,
            ref: element,
            "data-island-host": props.hostKey,
        });
    };
}

export const ReactEditor = presentationHost(connectReact);
export const VueEditor = presentationHost(connectVue);
