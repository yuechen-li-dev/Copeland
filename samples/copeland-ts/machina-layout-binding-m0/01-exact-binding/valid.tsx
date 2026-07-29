import { createElement } from "react";

layout type PageShell { column root { slot header; slot content; slot footer; } }
layout Page<0px, 0px> satisfies PageShell {
    width: 800px;
    height: 600px;
    column root { slot header { height: 64px; } slot content { height: fill; } slot footer { height: 48px; } }
}
function Header(): ReactNode { return <header></header>; }
function Content(): ReactNode { return <main></main>; }
function Footer(): ReactNode { return <footer></footer>; }
bind Page { header: Header(); content: Content(); footer: Footer(); }
