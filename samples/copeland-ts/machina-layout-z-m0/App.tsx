import { createElement } from "react";

layers AppLayers {
    content;
    overlay;
    modal;
}

function Page(): ReactNode { return <div>page</div>; }
function Dialog(): ReactNode { return <div>dialog</div>; }
function Tooltip(): ReactNode { return <div>tooltip</div>; }

stream DialogScene<0px, 0px> {
    layers: AppLayers;
    width: 320px;
    height: 180px;
    overlay root {
        page: Page() { frame: { x: 0px, y: 0px, width: 320px, height: 180px }; layer: content; z: 5; }
        dialog: Dialog() { frame: { x: 20px, y: 20px, width: 260px, height: 120px }; layer: modal; }
        tooltip: Tooltip() { frame: { x: 40px, y: 40px, width: 160px, height: 40px }; layer: modal; z: 1; }
    }
}

export function Main(): ReactNode { return DialogSceneStream(); }
