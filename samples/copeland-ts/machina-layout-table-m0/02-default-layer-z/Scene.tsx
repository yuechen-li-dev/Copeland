import { createElement } from "react";
function Page(): ReactNode { return <span>Page</span>; }
stream Scene<0px, 0px> {
    width: 100px;
    height: 80px;
    csv overlay root {
        name, content, x, y, width, height;
        page, Page(), 0px, 0px, 100px, 80px;
    }
}
