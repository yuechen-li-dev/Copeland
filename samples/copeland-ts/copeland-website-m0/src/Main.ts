import { dispatchReact, getMountElement, getViewportWidth, subscribeViewport } from "@copeland/browser-v1";
import { CopelandSite } from "./App";
import { ClassifyViewport } from "./LayoutProfiles";
import { MountReactRenderer, UnmountReactRenderer, UpdateReactRenderer } from "./ReactRendererAdapter";

export record SiteState {
    profile: int;
}

function InitialState(): SiteState {
    return { profile: ClassifyViewport(getViewportWidth()) };
}

function Reduce(state: SiteState, profile: int): SiteState {
    return state with { profile };
}

export function Main(): void {
    const root: ReactRoot = MountReactRenderer(getMountElement("app"));
    const send: (profile: int) => void = dispatchReact<SiteState, int>(
        InitialState(),
        Reduce,
        capture { root } (state: SiteState, send: (profile: int) => void) => {
            UpdateReactRenderer(root, CopelandSite(state.profile));
        });

    subscribeViewport(capture { send } () => send(ClassifyViewport(getViewportWidth())));
}
