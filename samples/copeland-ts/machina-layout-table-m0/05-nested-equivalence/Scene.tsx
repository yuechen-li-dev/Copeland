import { createElement } from "react";
function Page(): ReactNode { return <span>Page</span>; }
function Dialog(): ReactNode { return <span>Dialog</span>; }
stream TableScene<0px, 0px> {
    width: 320px;
    height: 180px;
    csv overlay root {
        name, content, x, y, width, height, z;
        page, Page(), 0px, 0px, 320px, 180px, 0;
        dialog, Dialog(), 20px, 20px, 260px, 120px, 1;
    }
}
