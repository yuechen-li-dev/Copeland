import { createElement } from "react";
import { createRoot } from "react-dom/client";
import { getMountElement } from "@copeland/browser-v1";

layers AppLayers {
    background;
    content;
    overlay;
    modal;
}

function Box(label: string, tone: string): ReactNode {
    return <div className={"proof-box " + tone}>{label}</div>;
}

function Background(): ReactNode { return Box("background / z 5", "background"); }
function Content(): ReactNode { return Box("content / z 0", "content"); }
function LowOverlay(): ReactNode { return Box("overlay / z -1", "low-overlay"); }
function EarlyOverlay(): ReactNode { return Box("early overlay / z 0", "early-overlay"); }
function LateOverlay(): ReactNode { return Box("late overlay / z 0", "late-overlay"); }
function HighOverlay(): ReactNode { return Box("overlay / z 5", "high-overlay"); }
function Modal(): ReactNode { return Box("modal / z -5", "modal"); }
function DefaultEarly(): ReactNode { return Box("default early", "default-early"); }
function DefaultLate(): ReactNode { return Box("default late", "default-late"); }

stream LayerProof<0px, 0px> {
    layers: AppLayers;
    width: 640px;
    height: 480px;
    overlay root {
        backgroundBox: Background() { frame: { x: 40px, y: 40px, width: 560px, height: 360px }; layer: background; z: 5; }
        contentBox: Content() { frame: { x: 80px, y: 80px, width: 480px, height: 280px }; layer: content; z: 0; }
        lowOverlay: LowOverlay() { frame: { x: 180px, y: 160px, width: 280px, height: 130px }; layer: overlay; z: -1; }
        earlyOverlay: EarlyOverlay() { frame: { x: 180px, y: 320px, width: 220px, height: 60px }; layer: overlay; z: 0; }
        lateOverlay: LateOverlay() { frame: { x: 180px, y: 320px, width: 220px, height: 60px }; layer: overlay; z: 0; }
        highOverlay: HighOverlay() { frame: { x: 180px, y: 160px, width: 280px, height: 130px }; layer: overlay; z: 5; }
        modalBox: Modal() { frame: { x: 250px, y: 220px, width: 190px, height: 130px }; layer: modal; z: -5; }
    }
}

stream DefaultProof<0px, 500px> {
    width: 200px;
    height: 100px;
    overlay root {
        defaultEarly: DefaultEarly() { frame: { x: 0px, y: 0px, width: 200px, height: 100px }; }
        defaultLate: DefaultLate() { frame: { x: 0px, y: 0px, width: 200px, height: 100px }; }
    }
}

function View(): ReactNode {
    return <div className="proof-page">{LayerProofStream()}{DefaultProofStream()}</div>;
}

export function Main(): void {
    const root: ReactRoot = createRoot(getMountElement("app"));
    root.render(View());
}
