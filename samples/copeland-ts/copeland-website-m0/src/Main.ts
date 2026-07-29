import { createRoot } from "react-dom/client";
import { dispatchReact, getMountElement } from "@copeland/browser-v1";
import { CopelandSite } from "./App";
import { CloseMobileMenuEvent, PrimaryCopiedEvent, PrimaryCopyFailedEvent, SecondaryCopiedEvent, SecondaryCopyFailedEvent, ToggleMobileMenuEvent } from "./Events";

record SiteState {
    mobileMenuOpen: boolean;
    primaryCopyLabel: string;
    secondaryCopyLabel: string;
}

function InitialState(): SiteState {
    return {
        mobileMenuOpen: false,
        primaryCopyLabel: "Copy primary command",
        secondaryCopyLabel: "Copy secondary command",
    };
}

function Reduce(state: SiteState, event: number): SiteState {
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
    dispatchReact<SiteState, number>(
        InitialState(),
        Reduce,
        capture { root } (state: SiteState, send: (event: number) => void) => root.render(CopelandSite(state.mobileMenuOpen, state.primaryCopyLabel, state.secondaryCopyLabel, send)));
}
