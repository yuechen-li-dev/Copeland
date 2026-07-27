import { createElement } from "react";
import { AppEvent, AppState } from "./State";

export function View(state: AppState, send: (event: AppEvent) => void): ReactNode {
    const countPrefix: string = "Count: ";
    const increment: AppEvent = AppEvent.Increment;
    return (
        <main>
            <h1>Copeland TS + React</h1>
            <p id="count">{countPrefix}{state.count}</p>
            <button id="increment" onClick={capture { send, increment } () => send(increment)}>
                Increment
            </button>
        </main>
    );
}
