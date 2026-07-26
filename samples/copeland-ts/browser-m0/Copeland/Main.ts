import { Increment } from "./Counter";
import { onClick, setText } from "@copeland/browser-m0";

export function Main(): void {
    const countElement: string = "count";
    setText(countElement, "0");
    onClick("increment", capture { countElement } (current: int): int => {
        const next: int = Increment(current);
        setText(countElement, String.From(next));
        return next;
    });
}
