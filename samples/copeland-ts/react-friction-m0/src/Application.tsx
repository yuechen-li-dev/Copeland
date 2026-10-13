import { presentViewport } from "@copeland/browser-v1";
import { View } from "./App";
import { AppEvent, AppState, InitialState, Reduce } from "./State";

export function Application(width: int): ReactNode {
    state: AppState = InitialState(width);

    on Apply(event: AppEvent) => Reduce(state, event);

    return presentViewport(View(state, on.Apply),
        capture { send: on.Apply } (width: int) => send(AppEvent.Resize(width)));
}
