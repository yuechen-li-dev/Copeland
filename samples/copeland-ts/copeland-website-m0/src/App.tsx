import { createElement } from "react";
import { copyText } from "@copeland/browser-v1";

function Copy(command: string): void {
    copyText(command, () => {}, () => {});
}

function Brand(): ReactNode {
    return <a className="brand" href="#overview"><span className="brand-mark">C</span><span><strong>Copeland TS</strong><small>AI-native TypeScript</small></span></a>;
}

function CommandBar(): ReactNode {
    return <aside className="command-bar">
        {Brand()}
        <nav>
            <a href="#overview">Overview</a>
            <a href="#features">Features</a>
            <a href="#architecture">Architecture</a>
            <a href="#tables">Tables</a>
        </nav>
        <p className="command-bar-proof">One language<br /><strong>React + .NET + npm</strong></p>
    </aside>;
}

function CompactCommandBar(): ReactNode {
    return <header className="compact-command-bar">{Brand()}<nav><a href="#features">Features</a><a href="#architecture">Architecture</a><a href="#tables">Tables</a></nav></header>;
}

function HeroCopy(): ReactNode {
    return <div className="hero-copy">
        <p className="eyebrow">ONE LANGUAGE / REAL BOUNDARIES</p>
        <h1>AI-native TypeScript for the next ChatGPT.</h1>
        <p>Copeland TS unifies React, .NET, npm, templates, and typed browser-to-CLR workflows—so AI writes less glue code and more product.</p>
        <div className="hero-actions"><a href="#features">Explore the model</a><a href="#architecture">See architecture</a></div>
    </div>;
}

function CodeBadge(): ReactNode {
    return <pre className="code-badge"><code>stream Copeland — featureGrid: Features();</code></pre>;
}

function HeroAccent(): ReactNode {
    return <p className="hero-accent">components + streams + tables</p>;
}

function HeroHalo(): ReactNode {
    return <div className="hero-halo"></div>;
}

function LanguageExample(): ReactNode {
    return <section className="language-example"><span>TypeScript</span><span className="arrow">→</span><span>TableScript</span><span className="arrow">→</span><span>TemplateScript</span></section>;
}

function FeatureCard(symbol: string, title: string, copy: string): ReactNode {
    return <article className="feature-card"><span>{symbol}</span><h2>{title}</h2><p>{copy}</p></article>;
}

function BridgeCard(): ReactNode { return FeatureCard("⌇⌁⌇", "Typed browser ↔ CLR", "End-to-end types across browser and .NET without a JSON tax."); }
function ReactCard(): ReactNode { return FeatureCard("◉ ◈", "React components", "Use ordinary third-party React components beneath generated hosts."); }
function TemplatesCard(): ReactNode { return FeatureCard("▤ </>", "Static templates", "Bounded compile-time structure with no hidden second runtime."); }
function TablesCard(): ReactNode { return FeatureCard("▦ ⎇", "Inspectable tables", "Typed, diffable, reviewable layout facts stored as ordinary source."); }

function Architecture(): ReactNode {
    return <section id="architecture" className="architecture"><p className="eyebrow">CANONICAL PIPELINE</p><h2>Components execute. Layouts describe spatial relations.</h2><div className="architecture-steps"><p>1. Components provide behavior and content.</p><p>2. Streams bind that content to named regions.</p><p>3. Compiler-projected tables produce neutral browser hosts.</p></div></section>;
}

function CallToAction(): ReactNode {
    return <section className="call-to-action"><div><p className="eyebrow">START WITH A REAL HOST</p><h2>Build the bridge, then inspect the tables.</h2></div><div className="command-actions"><button onClick={() => Copy("dotnet new copeland-react")}>Copy starter command</button><code>dotnet new copeland-react</code></div></section>;
}

function Footer(): ReactNode {
    return <footer className="site-footer"><span>Copeland TS / React / Machina / TSPack</span><a href="#overview">Back to overview ↑</a></footer>;
}

// The three roots deliberately have independent topology. Their nested hosts
// are the page geometry; the components above do not receive layout classes.
stream CopelandDesktop<0px, 0px> {
    width: 1440px;
    height: 900px;
    row root {
        commandBar: CommandBar() { width: 240px; height: fill; }
        column page { width: fill; height: fill;
            overlay hero { height: 362px;
                heroHalo: HeroHalo() { } with expandFrom(heroCopy, 18px);
                heroCopy: HeroCopy() { x: 52px; y: 76px; width: 600px; height: 226px; }
                heroAccent: HeroAccent() { width: 280px; height: 28px; } with centerXIn(heroCopy);
                codeBadge: CodeBadge() { y: 106px; width: 300px; height: 118px; } with placeRightOf(heroCopy, 32px);
            }
            languageExample: LanguageExample() { height: 56px; }
            grid featureGrid: [BridgeCard(), ReactCard(), TemplatesCard(), TablesCard()] { columns: 4; gap: 16px; height: 230px; }
            architecture: Architecture() { height: 148px; }
            callToAction: CallToAction() { height: 68px; }
            footer: Footer() { height: 36px; }
        }
    }
}

stream CopelandTablet<0px, 0px> {
    width: 768px;
    height: 1024px;
    column root {
        commandBar: CompactCommandBar() { height: 72px; }
        overlay hero { height: 318px;
            heroHalo: HeroHalo() { } with expandFrom(heroCopy, 16px);
            heroCopy: HeroCopy() { x: 42px; y: 62px; width: 480px; height: 204px; }
            heroAccent: HeroAccent() { width: 270px; height: 28px; } with centerXIn(heroCopy);
            codeBadge: CodeBadge() { x: 540px; y: 46px; width: 190px; height: 90px; }
        }
        languageExample: LanguageExample() { height: 58px; }
        grid featureGrid: [BridgeCard(), ReactCard(), TemplatesCard(), TablesCard()] { columns: 2; gap: 14px; height: 286px; }
        architecture: Architecture() { height: 152px; }
        callToAction: CallToAction() { height: 92px; }
        footer: Footer() { height: 46px; }
    }
}

stream CopelandMobile<0px, 0px> {
    width: 390px;
    height: 1620px;
    column root {
        commandBar: CompactCommandBar() { height: 74px; }
        overlay hero { height: 344px;
            heroHalo: HeroHalo() { } with expandFrom(heroCopy, 12px);
            heroCopy: HeroCopy() { x: 22px; y: 64px; width: 346px; height: 228px; }
            heroAccent: HeroAccent() { width: 250px; height: 28px; } with centerXIn(heroCopy);
            codeBadge: CodeBadge() { width: 346px; height: 36px; } with centerXIn(hero) with placeBelow(heroCopy, 8px);
        }
        languageExample: LanguageExample() { height: 62px; }
        grid featureGrid: [BridgeCard(), ReactCard(), TemplatesCard(), TablesCard()] { columns: 1; gap: 12px; height: 488px; }
        architecture: Architecture() { height: 250px; }
        callToAction: CallToAction() { height: 244px; }
        footer: Footer() { height: 78px; }
    }
}

export function CopelandSite(profile: int): ReactNode {
    if (profile == 3) {
        return CopelandDesktopStream();
    }

    if (profile == 2) {
        return CopelandTabletStream();
    }

    return CopelandMobileStream();
}
