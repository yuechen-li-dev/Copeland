import { dispatchReact, getMountElement, getViewportWidth, subscribeViewport } from "@copeland/browser-v1";
import { CopelandSite } from "./App";
import { ClassifyViewport } from "./LayoutProfiles";
import { MountHostAttachment, UpdateHostAttachment } from "./HostAttachmentRuntime";

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
    const attachmentId: string = "website::root::attachment";
    const componentInstanceId: string = "website::root";
    const root: ReactRoot = MountHostAttachment(attachmentId, componentInstanceId, getMountElement("app"), CopelandSite(InitialState().profile));
    const send: (profile: int) => void = dispatchReact<SiteState, int>(
        InitialState(),
        Reduce,
        capture { attachmentId, root } (state: SiteState, send: (profile: int) => void) => {
            UpdateHostAttachment(attachmentId, root, CopelandSite(state.profile));
        });

    subscribeViewport(capture { send } () => send(ClassifyViewport(getViewportWidth())));
}
