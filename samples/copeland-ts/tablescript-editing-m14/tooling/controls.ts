import { createElement, type FormEvent, type ReactNode } from "react";

// The host forwards text unchanged. Copeland owns drafts, parsing and policy.
export function TextField(props: {
    id: string;
    label: string;
    value: string;
    invalid: boolean;
    describedBy: string;
    inputMode: string;
    onValueChange(value: string): void;
}): ReactNode {
    return createElement("div", { className: "field" },
        createElement("label", { htmlFor: props.id }, props.label),
        createElement("input", {
            id: props.id,
            type: "text",
            inputMode: props.inputMode === "decimal" ? "decimal" : "numeric",
            value: props.value,
            "aria-invalid": props.invalid,
            "aria-describedby": props.describedBy,
            onChange: (event: React.ChangeEvent<HTMLInputElement>) => props.onValueChange(event.currentTarget.value),
        }));
}

export function PreviewForm(props: { children?: ReactNode; onCommit(): void }): ReactNode {
    function submit(event: FormEvent<HTMLFormElement>): void {
        event.preventDefault();
        props.onCommit();
    }
    return createElement("form", { onSubmit: submit, noValidate: true }, props.children);
}
