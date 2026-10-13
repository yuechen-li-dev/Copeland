import { createElement, type ReactNode } from "react";
import { Input } from "@base-ui-components/react/input";
import { Toggle } from "@base-ui-components/react/toggle";

interface SearchProps {
    id: string;
    type?: string;
    value: string;
    onValueChange(value: string): void;
}

export function SearchField(props: SearchProps): ReactNode {
    return createElement(Input, props);
}

export function Panel(props: {
    label: string;
    children?: ReactNode;
}): ReactNode {
    return createElement(
        "section",
        { "aria-label": props.label },
        props.children,
    );
}

export function ArrangementToggle(props: {
    value: boolean;
    onValueChange(value: boolean): void;
    children?: ReactNode;
}): ReactNode {
    return createElement(
        Toggle,
        {
            pressed: props.value,
            onPressedChange: props.onValueChange,
        },
        props.children,
    );
}
