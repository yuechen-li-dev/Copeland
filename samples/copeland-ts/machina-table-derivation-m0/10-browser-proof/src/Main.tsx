import { createElement } from "react";
import { createRoot } from "react-dom/client";
import { getMountElement } from "@copeland/browser-v1";

function Box(label: string, tone: string): ReactNode { return <div className={"box " + tone}>{label}</div>; }
function Page(): ReactNode { return Box("page", "page"); }
function Dialog(): ReactNode { return Box("dialog", "dialog"); }
function Tooltip(): ReactNode { return Box("tooltip", "tooltip"); }
function Backdrop(): ReactNode { return Box("backdrop", "backdrop"); }

stream DialogScene<0px, 0px> {
    width: 640px;
    height: 400px;
    overlay root {
        page: Page() { x: 0px; y: 0px; width: 640px; height: 400px; }
        dialog: Dialog() { width: 240px; height: 120px; } with centerIn(root);
        tooltip: Tooltip() { width: 140px; height: 40px; }
            with placeAbove(dialog, 12px)
            with alignRight(dialog);
        backdrop: Backdrop() { } with expandFrom(dialog, 20px);
    }
}

function View(): ReactNode { return <div className="scene">{DialogSceneStream()}</div>; }
export function Main(): void { const root: ReactRoot = createRoot(getMountElement("app")); root.render(View()); }
