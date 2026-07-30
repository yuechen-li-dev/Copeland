import { createElement } from "react";
layers Layers { content; overlay; }
function A(): ReactNode { return <span>A</span>; }
function B(): ReactNode { return <span>B</span>; }
stream Scene<0px, 0px> {
    layers: Layers;
    width: 100px;
    height: 80px;
    csv overlay root {
        name, content, x, y, width, height, layer, z;
        early, A(), 0px, 0px, 100px, 80px, overlay, 0;
        late, B(), 0px, 0px, 100px, 80px, overlay, 0;
    }
}
