import { createElement } from "react";
import { createRoot } from "react-dom/client";
import { getMountElement } from "@copeland/browser-v1";

layers AppLayers { content; modal; }
function Box(label: string, tone: string): ReactNode { return <div className={"box " + tone}>{label}</div>; }
function Page(): ReactNode { return Box("page", "page"); }
function Dialog(): ReactNode { return Box("dialog", "dialog"); }
function Tooltip(): ReactNode { return Box("tooltip", "tip"); }

stream DialogScene<0px, 0px> {
    layers: AppLayers;
    width: 320px;
    height: 180px;
    csv overlay root {
        name, content, x, y, width, height, layer, z;
        page, Page(), 0px, 0px, 320px, 180px, content, 0;
        dialog, Dialog(), 20px, 20px, 260px, 120px, modal, 0;
        tooltip, Tooltip(), 70px, 55px, 160px, 40px, modal, 1;
    }
}

function View(): ReactNode { return <div className="scene">{DialogSceneStream()}</div>; }
export function Main(): void { const root: ReactRoot = createRoot(getMountElement("app")); root.render(View()); }
