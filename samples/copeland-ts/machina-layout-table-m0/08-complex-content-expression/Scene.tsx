import { createElement } from "react";
function Card(title: string, description: string): ReactNode { return <span>{title}{description}</span>; }
stream Scene<0px, 0px> {
    width: 100px;
    height: 80px;
    csv overlay root {
        name, content, x, y, width, height;
        card, Card("Title", "Description"), 0px, 0px, 100px, 80px;
    }
}
