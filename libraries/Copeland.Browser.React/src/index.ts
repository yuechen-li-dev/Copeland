import { createElement, type ChangeEvent, type FormEvent, type ReactNode } from "react";

export interface TextFieldProps {
    id: string;
    label: string;
    value: string;
    invalid: boolean;
    describedBy: string;
    inputMode: string;
    onValueChange(value: string): void;
}

export interface PreviewFormProps {
    children?: ReactNode;
    onCommit(): void;
}

// The host forwards text unchanged. Copeland owns drafts, parsing and policy.
export function TextField(props: TextFieldProps): ReactNode {
    let inputMode: "text" | "numeric" | "decimal" = "numeric";
    if (props.inputMode === "text") {
        inputMode = "text";
    } else if (props.inputMode === "decimal") {
        inputMode = "decimal";
    }
    return createElement("div", { className: "field" },
        createElement("label", { htmlFor: props.id }, props.label),
        createElement("input", {
            id: props.id,
            type: "text",
            inputMode,
            value: props.value,
            "aria-invalid": props.invalid,
            "aria-describedby": props.describedBy,
            onChange: (event: ChangeEvent<HTMLInputElement>) => {
                props.onValueChange(event.currentTarget.value);
            },
        }));
}

export function PreviewForm(props: PreviewFormProps): ReactNode {
    function submit(event: FormEvent<HTMLFormElement>): void {
        event.preventDefault();
        props.onCommit();
    }
    return createElement("form", { onSubmit: submit, noValidate: true }, props.children);
}
