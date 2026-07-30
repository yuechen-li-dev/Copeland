import { createElement } from "react";
function Page(): ReactNode { return <span>Page</span>; }
stream Scene<0px, 0px> {
    width: 320px;
    height: 180px;
    row shell {
        column main {
            width: fill;
            height: fill;
            csv overlay scene {
                name, content, x, y, width, height;
                page, Page(), 0px, 0px, 320px, 180px;
            }
        }
    }
}
