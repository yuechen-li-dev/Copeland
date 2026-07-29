import { createElement } from "react";
import { copyText } from "@copeland/browser-v1";
import { CloseMobileMenuEvent, PrimaryCopiedEvent, PrimaryCopyFailedEvent, SecondaryCopiedEvent, SecondaryCopyFailedEvent, SelectSectionEvent, ToggleMobileMenuEvent } from "./Events";
import { DesktopProfile, TabletProfile } from "./LayoutProfiles";
import { MachinaDesktopRoot_0_0Class, MachinaDesktopRoot_0_1_0Class, MachinaDesktopRoot_0_1_1Class, MachinaDesktopRoot_0_1_2_0Class, MachinaDesktopRoot_0_1_2_1Class, MachinaDesktopRoot_0_1_2_2Class, MachinaDesktopRoot_0_1_2_3Class, MachinaDesktopRoot_0_1_2Class, MachinaDesktopRoot_0_1_3Class, MachinaDesktopRoot_0_1Class, MachinaDesktopRoot_0Class, MachinaDesktopRootClass } from "./generated/MachinaDesktopLayout";
import { MachinaTabletRoot_0_0Class, MachinaTabletRoot_0_1Class, MachinaTabletRoot_0_2Class, MachinaTabletRoot_0_3_0_0Class, MachinaTabletRoot_0_3_0_1Class, MachinaTabletRoot_0_3_0Class, MachinaTabletRoot_0_3_1_0Class, MachinaTabletRoot_0_3_1_1Class, MachinaTabletRoot_0_3_1Class, MachinaTabletRoot_0_3Class, MachinaTabletRoot_0_4Class, MachinaTabletRoot_0Class, MachinaTabletRootClass } from "./generated/MachinaTabletLayout";
import { MachinaMobileRoot_0_0Class, MachinaMobileRoot_0_1Class, MachinaMobileRoot_0_2Class, MachinaMobileRoot_0_3Class, MachinaMobileRoot_0_4Class, MachinaMobileRoot_0_5_0Class, MachinaMobileRoot_0_5_1Class, MachinaMobileRoot_0_5_2Class, MachinaMobileRoot_0_5_3Class, MachinaMobileRoot_0_5Class, MachinaMobileRoot_0_6Class, MachinaMobileRoot_0Class, MachinaMobileRootClass } from "./generated/MachinaMobileLayout";

export record SiteState {
    activeSection: int;
    mobileMenuOpen: boolean;
    primaryCopyLabel: string;
    profile: int;
    secondaryCopyLabel: string;
}

function CopyCommand(command: string, copied: int, failed: int, send: (event: int) => void): void {
    copyText(
        command,
        capture { send, copied } () => send(copied),
        capture { send, failed } () => send(failed));
}

function Brand(): ReactNode {
    return <a className="brand" href="#overview"><span className="brand-mark">C</span><span><strong>Copeland TS</strong><small>AI-native TypeScript</small></span></a>;
}

function SelectNavigationItem(send: (event: int) => void, section: int, closeMenu: boolean): void {
    send(SelectSectionEvent(section));
    if (closeMenu) {
        send(CloseMobileMenuEvent());
    }
}

function NavigationItem(label: string, target: string, section: int, state: SiteState, send: (event: int) => void, closeMenu: boolean): ReactNode {
    let className = "navigation-item";
    if (state.activeSection == section) {
        className = "navigation-item navigation-item-active";
    }

    return <a className={className} href={target} onClick={capture { send, section, closeMenu } () => SelectNavigationItem(send, section, closeMenu)}>{label}</a>;
}

function Navigation(state: SiteState, send: (event: int) => void, className: string, closeMenu: boolean): ReactNode {
    return <nav className={className}>
        {NavigationItem("Overview", "#overview", 1, state, send, closeMenu)}
        {NavigationItem("React", "#react", 2, state, send, closeMenu)}
        {NavigationItem("Browser Bridge", "#bridge", 3, state, send, closeMenu)}
        {NavigationItem("Machina", "#machina", 4, state, send, closeMenu)}
        {NavigationItem("Templates", "#templates", 5, state, send, closeMenu)}
        {NavigationItem("Tables", "#tables", 6, state, send, closeMenu)}
        {NavigationItem("TSPack", "#tspack", 7, state, send, closeMenu)}
    </nav>;
}

function DesktopSidebar(state: SiteState, send: (event: int) => void): ReactNode {
    return <aside className="desktop-sidebar">
        {Brand()}
        {Navigation(state, send, "desktop-navigation", false)}
        <div className="sidebar-proof"><span>Copeland TS</span><strong>1 language</strong><i>+</i><strong>2 ecosystems united</strong></div>
        <footer><span>v0.1.0</span><span className="status-dot">All systems go</span></footer>
    </aside>;
}

function TabletHeader(state: SiteState, send: (event: int) => void, className: string): ReactNode {
    return <header className={"tablet-header " + className}>{Brand()}{Navigation(state, send, "tablet-navigation", false)}</header>;
}

function MobileHeader(state: SiteState, send: (event: int) => void, className: string): ReactNode {
    let menuClass = "mobile-navigation";
    if (state.mobileMenuOpen) {
        menuClass = "mobile-navigation mobile-navigation-open";
    }

    return <header className={"mobile-header " + className}>
        {Brand()}
        <button className="menu-button" onClick={capture { send } () => send(ToggleMobileMenuEvent())}><span></span><span></span><span></span><span className="sr-only">Toggle navigation</span></button>
        {Navigation(state, send, menuClass, true)}
    </header>;
}

function CommandCard(command: string, copyLabel: string, copied: int, failed: int, send: (event: int) => void, primary: boolean): ReactNode {
    let className = "command-card";
    if (primary) {
        className = "command-card primary-command";
    }

    return <div className={className}><code><span>›_</span>{command}</code><button onClick={capture { command, copied, failed, send } () => CopyCommand(command, copied, failed, send)}>{copyLabel}</button></div>;
}

function CommandPanel(state: SiteState, send: (event: int) => void): ReactNode {
    return <div className="commands" id="commands">
        {CommandCard("dotnet new copeland-react", state.primaryCopyLabel, PrimaryCopiedEvent(), PrimaryCopyFailedEvent(), send, true)}
        {CommandCard("tscl build • tspack run", state.secondaryCopyLabel, SecondaryCopiedEvent(), SecondaryCopyFailedEvent(), send, false)}
    </div>;
}

function CapabilityFlow(): ReactNode {
    return <div className="capabilities"><span>◌ React works</span><span>npm works</span><span>CLR works</span><span>◈ Machina UI</span><span>▦ Tables</span><span>▤ Templates</span><span>⌘ VS Code</span><span>ϟ TSPack</span></div>;
}

function CodeTexture(): ReactNode {
    return <div className="code-texture"></div>;
}

function HeroWords(): ReactNode {
    return <div className="hero-words"><p className="eyebrow">ONE LANGUAGE / REAL BOUNDARIES</p><h1>AI-native TypeScript for<br />the next ChatGPT.</h1><h2>the next ChatGPT is still ChatGPT.</h2><p className="hero-copy">Copeland TS unifies React, .NET, npm, templates, and typed browser-to-CLR workflows—so AI writes less glue code and more product.</p></div>;
}

function Announcement(): ReactNode {
    return <div className="announcement">● Now shipping • GPT-5.6 demo</div>;
}

function ProofCard(): ReactNode {
    return <aside className="live-proof"><span>● LIVE PROOF</span><strong>compiler<br />tests</strong><p>Copeland TS<br />TSPack materialized</p></aside>;
}

function DesktopHero(state: SiteState, send: (event: int) => void, className: string): ReactNode {
    return <section id="overview" className={"hero desktop-hero " + className}>{CodeTexture()}{Announcement()}<div className="hero-content">{HeroWords()}{CommandPanel(state, send)}{CapabilityFlow()}</div>{ProofCard()}</section>;
}

function TabletHero(state: SiteState, send: (event: int) => void, className: string): ReactNode {
    return <section id="overview" className={"hero tablet-hero " + className}>{CodeTexture()}{Announcement()}<div className="hero-content">{HeroWords()}{CommandPanel(state, send)}{ProofCard()}{CapabilityFlow()}</div></section>;
}

function MobileHero(className: string): ReactNode {
    return <section id="overview" className={"hero mobile-hero " + className}>{CodeTexture()}{Announcement()}<div className="hero-content">{HeroWords()}</div></section>;
}

function LanguageStrip(className: string): ReactNode {
    return <section className={"language-strip " + className}><span>✦ ONE SOURCE OF TRUTH</span><strong>TypeScript <i>→</i> TableScript <i>→</i> TemplateScript</strong></section>;
}

function FeatureCard(id: string, symbol: string, title: string, copy: string, className: string): ReactNode {
    return <article id={id} className={"feature-card " + className}><span className="feature-symbol">{symbol}</span><h3>{title}</h3><p>{copy}</p><a href="#overview">Read more ›_</a></article>;
}

function FeatureCards(firstClass: string, secondClass: string, thirdClass: string, fourthClass: string): ReactNode {
    return <div className="feature-card-collection">{FeatureCard("bridge", "⌇⌁⌇", "Typed browser ↔ CLR bridge", "End-to-end types across browser and .NET. No JSON tax.", firstClass)}{FeatureCard("react", "◉ ◈", "Third-party React components", "Use the ecosystem you love. Fully typed. Zero wrappers.", secondClass)}{FeatureCard("templates", "▤ </>", "Static templates", "Bounded compile-time structure without a hidden second runtime.", thirdClass)}{FeatureCard("tables", "▦ ⎇", "Git-native tables", "Typed, diffable, reviewable data stored as ordinary source.", fourthClass)}</div>;
}

function Footer(className: string): ReactNode {
    return <footer id="tspack" className={"site-footer " + className}><span>Copeland TS / React / MachinaLayout / TSPack</span><a href="#overview">Back to overview ↑</a></footer>;
}

function DesktopSite(state: SiteState, send: (event: int) => void): ReactNode {
    return <div className={"site-profile desktop-site " + MachinaDesktopRootClass()}><div className={MachinaDesktopRoot_0Class()}><div className={MachinaDesktopRoot_0_0Class()}>{DesktopSidebar(state, send)}</div><main id="main-content" className={MachinaDesktopRoot_0_1Class()}>{DesktopHero(state, send, MachinaDesktopRoot_0_1_0Class())}{LanguageStrip(MachinaDesktopRoot_0_1_1Class())}<section className={"feature-grid " + MachinaDesktopRoot_0_1_2Class()}>{FeatureCards(MachinaDesktopRoot_0_1_2_0Class(), MachinaDesktopRoot_0_1_2_1Class(), MachinaDesktopRoot_0_1_2_2Class(), MachinaDesktopRoot_0_1_2_3Class())}</section>{Footer(MachinaDesktopRoot_0_1_3Class())}</main></div></div>;
}

function TabletSite(state: SiteState, send: (event: int) => void): ReactNode {
    return <div className={"site-profile tablet-site " + MachinaTabletRootClass()}><div className={MachinaTabletRoot_0Class()}>{TabletHeader(state, send, MachinaTabletRoot_0_0Class())}<main id="main-content">{TabletHero(state, send, MachinaTabletRoot_0_1Class())}{LanguageStrip(MachinaTabletRoot_0_2Class())}<section className={"feature-grid tablet-feature-grid " + MachinaTabletRoot_0_3Class()}><div className={MachinaTabletRoot_0_3_0Class()}>{FeatureCard("bridge", "⌇⌁⌇", "Typed browser ↔ CLR bridge", "End-to-end types across browser and .NET. No JSON tax.", MachinaTabletRoot_0_3_0_0Class())}{FeatureCard("react", "◉ ◈", "Third-party React components", "Use the ecosystem you love. Fully typed. Zero wrappers.", MachinaTabletRoot_0_3_0_1Class())}</div><div className={MachinaTabletRoot_0_3_1Class()}>{FeatureCard("templates", "▤ </>", "Static templates", "Bounded compile-time structure without a hidden second runtime.", MachinaTabletRoot_0_3_1_0Class())}{FeatureCard("tables", "▦ ⎇", "Git-native tables", "Typed, diffable, reviewable data stored as ordinary source.", MachinaTabletRoot_0_3_1_1Class())}</div></section>{Footer(MachinaTabletRoot_0_4Class())}</main></div></div>;
}

function MobileSite(state: SiteState, send: (event: int) => void): ReactNode {
    return <div className={"site-profile mobile-site " + MachinaMobileRootClass()}><div className={MachinaMobileRoot_0Class()}>{MobileHeader(state, send, MachinaMobileRoot_0_0Class())}<main id="main-content">{MobileHero(MachinaMobileRoot_0_1Class())}<section className={"mobile-command-stack " + MachinaMobileRoot_0_2Class()}>{CommandPanel(state, send)}</section><section className={"mobile-capability-flow " + MachinaMobileRoot_0_3Class()}>{CapabilityFlow()}</section>{LanguageStrip(MachinaMobileRoot_0_4Class())}<section className={"feature-list " + MachinaMobileRoot_0_5Class()}>{FeatureCards(MachinaMobileRoot_0_5_0Class(), MachinaMobileRoot_0_5_1Class(), MachinaMobileRoot_0_5_2Class(), MachinaMobileRoot_0_5_3Class())}</section>{Footer(MachinaMobileRoot_0_6Class())}</main></div></div>;
}

export function CopelandSite(profile: int, state: SiteState, send: (event: int) => void): ReactNode {
    if (profile == DesktopProfile()) {
        return DesktopSite(state, send);
    }

    if (profile == TabletProfile()) {
        return TabletSite(state, send);
    }

    return MobileSite(state, send);
}
