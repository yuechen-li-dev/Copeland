import { createRoot } from "react-dom/client";
import { getMountElement, getViewportWidth } from "@copeland/browser-v1";
import { Application } from "./Application";

export function Main(): void {
    const root: ReactRoot = createRoot(getMountElement("app"));
    root.render(Application(getViewportWidth()));
}
