import { createElement } from "react";
import { ownChild } from "@copeland/browser-v1";
import { focus } from "@copeland/lifecycle-host";
import { Editor } from "./Demo";

function FocusPhase(): void { focus("phase"); }

function RecordSave(): void { focus("missing"); }
export function Broken(): ReactNode {
    state: int = 0;
    on Change(value: int) => value effect RecordSave();
    const values: int[] = [1];
    return <span>{values[state]}</span>;
}

export function Duplicate(): ReactNode {
    state: int = 0;
    on Change() => state + 1;
    const children: ReactNode[] = DuplicateChildren(state);
    return <div>{children}<button onClick={on.Change}>Duplicate</button></div>;
}


function DuplicateChildren(value: int): ReactNode[] {
    if (value == 0) { return [ownChild("rows", "a", Editor("a", "A"))]; }
    return [ownChild("rows", "a", Editor("a", "A")), ownChild("rows", "a", Editor("a", "B"))];
}

export function SendText(send: (value: string) => void, value: string): void { send(value); }
export function SendNumber(send: (value: int) => void, value: int): void { send(value); }

function Immediate(): int { return 2; }
export function Phases(): ReactNode {
    state: int = 0;
    on Done(value: int) => value;
    on Run() => 1 effect Immediate() => Done(result)
        after PresentationCommitted effect FocusPhase();
    return <section aria-label="Phases"><span id="phase">{state}</span><button onClick={on.Run}>Run phases</button></section>;
}
