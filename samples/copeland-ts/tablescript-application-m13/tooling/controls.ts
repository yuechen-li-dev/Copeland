import { createElement, useEffect, useState, type ChangeEvent, type FormEvent, type ReactNode } from "react";

// DOM text-to-int adaptation only. Negative counts reach the application unchanged.
export function CountField(props: {
    id: string;
    label: string;
    value: number;
    resetKey: number;
    onValueChange(value: number): void;
}): ReactNode {
    // A foreign control owns transient DOM text, including incomplete/invalid text.
    // Copeland receives only a representable int; application policy stays outside.
    const [draft, setDraft] = useState(String(props.value));
    useEffect(() => {
        setDraft(String(props.value));
    }, [props.value, props.resetKey]);

    function change(event: ChangeEvent<HTMLInputElement>): void {
        const input = event.currentTarget;
        setDraft(input.value);
        const value = input.valueAsNumber;
        const valid = Number.isInteger(value) && value >= -2147483648 && value <= 2147483647;
        if (valid) {
            props.onValueChange(value);
        } else {
            input.reportValidity();
        }
    }
    return createElement("div", { className: "field" },
        createElement("label", { htmlFor: props.id }, props.label),
        createElement("input", {
            id: props.id,
            type: "number",
            step: 1,
            min: -2147483648,
            max: 2147483647,
            required: true,
            value: draft,
            "aria-describedby": "edit-error",
            onChange: change,
        }));
}

export function PreviewForm(props: { children?: ReactNode; onCommit(): void }): ReactNode {
    function submit(event: FormEvent<HTMLFormElement>): void {
        event.preventDefault();
        if (event.currentTarget.checkValidity()) {
            props.onCommit();
        }
    }
    return createElement("form", { onSubmit: submit }, props.children);
}
