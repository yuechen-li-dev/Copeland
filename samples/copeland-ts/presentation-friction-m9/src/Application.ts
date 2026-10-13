import { presentViewport } from "@copeland/browser-v1";
import { AppState, AppEvent, InitialState, Reduce } from "./State";
import { IssuesPage } from "./IssuesPage";

export function Application(width: int): View {
    state: AppState = InitialState(width);
    on Apply(event: AppEvent) => Reduce(state, event);
    return presentViewport(IssuesPage(state, on.Apply),
        capture { send: on.Apply } (width: int) => send(AppEvent.Resize(width)));
}
