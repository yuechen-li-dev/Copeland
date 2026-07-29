import { createRoot } from "react-dom/client";
import { dispatchReact, getMountElement, getViewportWidth, subscribeViewport } from "@copeland/browser-v1";
import { CopelandSite, SiteState } from "./App";
import { CloseMobileMenuEvent, DesktopProfileChangedEvent, IsSectionEvent, MobileProfileChangedEvent, PrimaryCopiedEvent, PrimaryCopyFailedEvent, SectionFromEvent, SecondaryCopiedEvent, SecondaryCopyFailedEvent, TabletProfileChangedEvent, ToggleMobileMenuEvent } from "./Events";
import { ClassifyViewport, DesktopProfile, IsMobileProfile, MobileProfile, TabletProfile } from "./LayoutProfiles";

function InitialState(): SiteState {
    return {
        activeSection: 1,
        mobileMenuOpen: false,
        primaryCopyLabel: "Copy primary command",
        profile: ClassifyViewport(getViewportWidth()),
        secondaryCopyLabel: "Copy secondary command",
    };
}

function ProfileFromEvent(event: int): int {
    if (event == DesktopProfileChangedEvent()) {
        return DesktopProfile();
    }

    if (event == TabletProfileChangedEvent()) {
        return TabletProfile();
    }

    return MobileProfile();
}

function ProfileChangedEvent(profile: int): int {
    if (profile == DesktopProfile()) {
        return DesktopProfileChangedEvent();
    }

    if (profile == TabletProfile()) {
        return TabletProfileChangedEvent();
    }

    return MobileProfileChangedEvent();
}

function Reduce(state: SiteState, event: int): SiteState {
    if (event == DesktopProfileChangedEvent() || event == TabletProfileChangedEvent() || event == MobileProfileChangedEvent()) {
        const profile = ProfileFromEvent(event);
        if (IsMobileProfile(profile)) {
            return state with { profile };
        }

        return state with { profile, mobileMenuOpen: false };
    }

    if (IsSectionEvent(event)) {
        return state with { activeSection: SectionFromEvent(event) };
    }

    if (event == ToggleMobileMenuEvent()) {
        return state with { mobileMenuOpen: !state.mobileMenuOpen };
    }

    if (event == CloseMobileMenuEvent()) {
        return state with { mobileMenuOpen: false };
    }

    if (event == PrimaryCopiedEvent()) {
        return state with { primaryCopyLabel: "Primary command copied" };
    }

    if (event == PrimaryCopyFailedEvent()) {
        return state with { primaryCopyLabel: "Copy unavailable" };
    }

    if (event == SecondaryCopiedEvent()) {
        return state with { secondaryCopyLabel: "Secondary command copied" };
    }

    if (event == SecondaryCopyFailedEvent()) {
        return state with { secondaryCopyLabel: "Copy unavailable" };
    }

    return state;
}

export function Main(): void {
    const root: ReactRoot = createRoot(getMountElement("app"));
    const send: (event: int) => void = dispatchReact<SiteState, int>(
        InitialState(),
        Reduce,
        capture { root } (state: SiteState, send: (event: int) => void) => root.render(CopelandSite(state.profile, state, send)));

    subscribeViewport(capture { send } () => send(ProfileChangedEvent(ClassifyViewport(getViewportWidth()))));
}
