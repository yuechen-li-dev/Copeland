import { createRoot } from "react-dom/client";
import { dispatchReact, getMountElement } from "@copeland/browser-v1";
import { BridgeError, SerializeState } from "./Bridge";
import { View } from "./App";
import { AppEvent, AppState, InitialState, Reduce } from "./State";

async function SerializeEffect(state: AppState, send: (event: AppEvent) => void): void {
    try {
        const pending: Async<string ! BridgeError> = SerializeState({ message: "Hello from CLR", count: state.count });
        const serialized: string = await pending?;
        send(AppEvent.SerializationCompleted(serialized))
    } except (error) {
        send(AppEvent.SerializationFailed("The CLR bridge request failed."))
    };
}

export function Main(): void {
    const root: ReactRoot = createRoot(getMountElement("app"));
    dispatchReact<AppState, AppEvent>(
        InitialState(),
        Reduce,
        capture { root } (state: AppState, send: (event: AppEvent) => void) => {
            root.render(View(state, send));
            if (state.loading) {
                SerializeEffect(state, send);
            }
        });
}
