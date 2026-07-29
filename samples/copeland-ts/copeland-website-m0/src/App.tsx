import { createElement } from "react";
import { copyText } from "@copeland/browser-v1";
import { CloseMobileMenuEvent, PrimaryCopiedEvent, PrimaryCopyFailedEvent, SecondaryCopiedEvent, SecondaryCopyFailedEvent, ToggleMobileMenuEvent } from "./Events";
import { MachinaHeroRootClass, MachinaHeroRoot_0_0Class, MachinaHeroRoot_0_1Class, MachinaHeroRoot_0_2Class, MachinaHeroRoot_0_3_0Class, MachinaHeroRoot_0_3_1Class, MachinaHeroRoot_0_3Class, MachinaHeroRoot_0Class } from "./generated/MachinaHero";

function CopyCommand(command: string, copied: number, failed: number, send: (event: number) => void): void {
    copyText(
        command,
        capture { send, copied } () => send(copied),
        capture { send, failed } () => send(failed));
}

function Brand(): ReactNode {
    return <a className="brand" href="#overview"><span className="brand-mark">C</span><span><strong>Copeland TS</strong><small>AI-native TypeScript</small></span></a>;
}

function CloseMobileMenu(send: (event: number) => void): void {
    send(CloseMobileMenuEvent());
}

function ToggleMobileMenu(send: (event: number) => void): void {
    send(ToggleMobileMenuEvent());
}

function Navigation(mobile: boolean, open: boolean, send: (event: number) => void): ReactNode {
    if (mobile) {
        if (open) {
            return <nav className="mobile-navigation mobile-navigation-open"><a href="#overview" onClick={capture { send } () => CloseMobileMenu(send)}>Overview</a><a href="#react">React</a><a href="#bridge">Browser Bridge</a><a href="#machina">Machina</a><a href="#templates">Templates</a><a href="#tables">Tables</a><a href="#tspack">TSPack</a></nav>;
        }

        return <nav className="mobile-navigation"><a href="#overview">Overview</a><a href="#react">React</a><a href="#bridge">Browser Bridge</a><a href="#machina">Machina</a><a href="#templates">Templates</a><a href="#tables">Tables</a><a href="#tspack">TSPack</a></nav>;
    }

    return <nav className="navigation"><a href="#overview">Overview</a><a href="#react">React</a><a href="#bridge">Browser Bridge</a><a href="#machina">Machina</a><a href="#templates">Templates</a><a href="#tables">Tables</a><a href="#tspack">TSPack</a></nav>;
}

function DesktopSidebar(send: (event: number) => void): ReactNode {
    return <aside className="sidebar">{Brand()}{Navigation(false, true, send)}<div className="sidebar-proof"><span>Copeland TS</span><strong>1 language</strong><i>+</i><strong>2 ecosystems united</strong></div><footer><span>v0.1.0</span><span className="status-dot">All systems go</span></footer></aside>;
}

function MobileHeader(mobileMenuOpen: boolean, send: (event: number) => void): ReactNode {
    return <header className="mobile-header">{Brand()}<button className="menu-button" onClick={capture { send } () => ToggleMobileMenu(send)}><span></span><span></span><span></span><span className="sr-only">Open navigation</span></button>{Navigation(true, mobileMenuOpen, send)}</header>;
}

function CommandCard(command: string, copyLabel: string, copied: number, failed: number, send: (event: number) => void, primary: boolean, machinaClass: string): ReactNode {
    if (primary) {
        return <div className={"command-card primary-command " + machinaClass}><code><span>›_</span>{command}</code><button onClick={capture { command, copied, failed, send } () => CopyCommand(command, copied, failed, send)}>{copyLabel}</button></div>;
    }

    return <div className={"command-card " + machinaClass}><code><span>›_</span>{command}</code><button onClick={capture { command, copied, failed, send } () => CopyCommand(command, copied, failed, send)}>{copyLabel}</button></div>;
}

function CapabilityChips(): ReactNode {
    return <div className="capabilities"><span>◌ React works</span><span>npm works</span><span>CLR works</span><span>◈ Machina UI</span><span>▦ Tables</span><span>▤ Templates</span><span>⌘ VS Code</span><span>ϟ TSPack</span></div>;
}

function CodeTexture(): ReactNode {
    return <div className="code-texture"></div>;
}

function HeroSection(primaryCopyLabel: string, secondaryCopyLabel: string, send: (event: number) => void): ReactNode {
    return <section id="overview" className="hero">{CodeTexture()}<div className="announcement">● Now shipping • GPT-5.6 demo</div><div className={"hero-content machina-hero " + MachinaHeroRootClass()}><div className={MachinaHeroRoot_0Class()}><p className="eyebrow native-eyebrow">ONE LANGUAGE / REAL BOUNDARIES</p><h1 className={MachinaHeroRoot_0_0Class()}>AI-native TypeScript for<br />the next ChatGPT.</h1><h2 className={MachinaHeroRoot_0_1Class()}>the next ChatGPT is still ChatGPT.</h2><p className={"hero-copy " + MachinaHeroRoot_0_2Class()}>Copeland TS unifies React, .NET, npm, templates, and typed browser-to-CLR workflows—so AI writes less glue code and more product.</p><div className={"commands " + MachinaHeroRoot_0_3Class()}>{CommandCard("dotnet new copeland-react", primaryCopyLabel, PrimaryCopiedEvent(), PrimaryCopyFailedEvent(), send, true, MachinaHeroRoot_0_3_0Class())}{CommandCard("tscl build • tspack run", secondaryCopyLabel, SecondaryCopiedEvent(), SecondaryCopyFailedEvent(), send, false, MachinaHeroRoot_0_3_1Class())}</div>{CapabilityChips()}</div></div><aside className="live-proof"><span>● LIVE PROOF</span><strong>compiler<br />tests</strong><p>Copeland TS<br />TSPack materialized</p></aside></section>;
}

function LanguageStrip(): ReactNode {
    return <section className="language-strip"><span>✦ ONE SOURCE OF TRUTH</span><strong>TypeScript <i>→</i> TableScript <i>→</i> TemplateScript</strong></section>;
}

function FeatureCard(id: string, symbol: string, title: string, copy: string): ReactNode {
    return <article id={id} className="feature-card"><span className="feature-symbol">{symbol}</span><h3>{title}</h3><p>{copy}</p><a href="#overview">›_</a></article>;
}

function FeatureGrid(): ReactNode {
    return <section className="feature-grid">{FeatureCard("bridge", "⌇⌁⌇", "Typed browser ↔ CLR bridge", "End-to-end types across browser and .NET. No JSON tax.")}{FeatureCard("react", "◉ ◈", "Third-party React components", "Use the ecosystem you love. Fully typed. Zero wrappers.")}{FeatureCard("templates", "▤ </>", "Static templates", "Bounded compile-time structure without a hidden second runtime.")}{FeatureCard("tables", "▦ ⎇", "Git-native tables", "Typed, diffable, reviewable data stored as ordinary source.")}</section>;
}

function Footer(): ReactNode {
    return <footer id="tspack" className="site-footer"><span>Copeland TS / React / MachinaLayout / TSPack</span><a href="#overview">Back to overview ↑</a></footer>;
}

export function CopelandSite(mobileMenuOpen: boolean, primaryCopyLabel: string, secondaryCopyLabel: string, send: (event: number) => void): ReactNode {
    return <div className="site-shell">{DesktopSidebar(send)}{MobileHeader(mobileMenuOpen, send)}<main>{HeroSection(primaryCopyLabel, secondaryCopyLabel, send)}{LanguageStrip()}{FeatureGrid()}{Footer()}</main></div>;
}
