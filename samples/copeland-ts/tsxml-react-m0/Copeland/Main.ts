import { View } from "./App";
import { AppEvent, AppState, InitialState, Reduce } from "./State";
import { createRoot } from "react-dom/client";
import { dispatchReact, getMountElement } from "@copeland/browser-v1";

export function Main(): void {
    const root: ReactRoot = createRoot(getMountElement("app"));
    dispatchReact<AppState, AppEvent>(
        InitialState(),
        Reduce,
        capture { root } (state: AppState, send: (event: AppEvent) => void) => root.render(View(state, send)));
}
