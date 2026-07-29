import { createElement } from "react";

layout type PageShell {
    column root {
        slot header;
        slot content;
        slot footer;
    }
}

layout Page<0px, 0px> satisfies PageShell {
    width: 800px;
    height: 600px;

    column root {
        slot header { height: 64px; }
        slot content { height: fill; }
        slot footer { height: 48px; }
    }
}

function Header(): ReactNode { return <span>Header</span>; }
function Content(): ReactNode { return <span>Content</span>; }
function Footer(): ReactNode { return <span>Footer</span>; }

bind Page {
    header: Header();
    content: Content();
    footer: Footer();
}

export function Main(): ReactNode {
    return PageBinding();
}
