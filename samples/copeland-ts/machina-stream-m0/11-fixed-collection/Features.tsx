import { createElement } from "react";

function BridgeCard(): ReactNode { return <span>Bridge</span>; }
function ReactCard(): ReactNode { return <span>React</span>; }
function TemplateCard(): ReactNode { return <span>Template</span>; }
function TableCard(): ReactNode { return <span>Table</span>; }

stream Features<0px, 0px> {
    width: 800px;
    height: 320px;

    grid features: [
        BridgeCard(),
        ReactCard(),
        TemplateCard(),
        TableCard()
    ] {
        columns: 4;
        gap: 16px;
        height: fill;
    }
}

export function Main(): ReactNode {
    return FeaturesStream();
}
