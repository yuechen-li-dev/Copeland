import { createElement } from "react";
import { Widget } from "@fixture/widget";
stream Scene<0px, 0px> {
    width: 100px;
    height: 80px;
    csv overlay root {
        name, content, x, y, width, height;
        widget, <Widget />, 0px, 0px, 100px, 80px;
    }
}
