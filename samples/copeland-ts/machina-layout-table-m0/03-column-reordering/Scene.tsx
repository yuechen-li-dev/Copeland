import { createElement } from "react";
function Page(): ReactNode { return <span>Page</span>; }
stream Scene<0px, 0px> {
    width: 100px;
    height: 80px;
    csv overlay root {
        content, height, name, width, y, x;
        Page(), 80px, page, 100px, 0px, 0px;
    }
}
