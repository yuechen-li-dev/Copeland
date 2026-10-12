import { createRoot } from "react-dom/client";
import { dispatchReact, getMountElement, getViewportWidth, subscribeViewport } from "@copeland/browser-v1";
import { View } from "./App";
import { AppEvent, AppState, InitialState, Reduce } from "./State";

export function Main(): void {
    const root: ReactRoot = createRoot(getMountElement("app"));
    const send: (event: AppEvent) => void = dispatchReact<AppState, AppEvent>(
        InitialState(getViewportWidth()),
        Reduce,
        capture { root } (state: AppState, send: (event: AppEvent) => void) => {
            root.render(View(state, send));
        });
    subscribeViewport(capture { send } () => send(AppEvent.Resize(getViewportWidth())));
}
