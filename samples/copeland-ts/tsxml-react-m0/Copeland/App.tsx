import { createElement } from "react";
import { AppEvent, AppState } from "./State";

export function View(state: AppState, send: (event: AppEvent) => void): ReactNode {
    const increment: AppEvent = AppEvent.Increment;
    return (
        <main>
            <h1>Copeland TS</h1>
            <p id="count">Count: {state.count}</p>
            <p>CLR JSON:</p>
            <pre id="serialized">{state.serialized}</pre>
            <p id="error">{state.error}</p>
            <button id="increment" onClick={capture { send, increment } () => send(increment)}>
                Increment
            </button>
        </main>
    );
}
