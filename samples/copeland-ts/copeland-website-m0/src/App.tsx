import { createElement } from "react";
import { copyText } from "@copeland/browser-v1";

record FeatureCardProps {
    icon: string;
    title: string;
    body: string;
}

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

function Text(value: string): ReactNode {
    return <p className="text-document text-body">{value}</p>;
}

// React remains an opaque child renderer. FeatureCard owns the compiler-visible
// private stream that contains this child and its typed props capture.
function RendererBoundaryBadge(): ReactNode {
    return <copeland-renderer-badge label="Custom Element"></copeland-renderer-badge>;
}

function FeatureCardContent(props: FeatureCardProps): ReactNode {
    return <article className="feature-card">
        <span className="feature-card-icon">{props.icon}</span>
        {RendererBoundaryBadge()}
        <h2>{props.title}</h2>
        <p>{props.body}</p>
    </article>;
}

// One reusable lexical capsule owns card presentation and bounded text
// containment. Its caller receives only one outer grid host and typed props.
function FeatureCard(props: FeatureCardProps): ReactNode {
    stream Surface<0px, 0px> {
        width: fill;
        height: fill;
        content: FeatureCardContent(props) { width: fill; height: fill; }
    }

    return Surface();
}

// Hero owns all local presentation. Profile is explicit rather than a set of
// responsive properties leaking into the page layout contract.
function HeroClass(profile: int): string {
    if (profile == 3) {
        return "hero-capsule hero-desktop";
    }

    if (profile == 2) {
        return "hero-capsule hero-tablet";
    }

    return "hero-capsule hero-mobile";
}

function HeroContent(profile: int): ReactNode {
    return <section className={HeroClass(profile)}>
        <div className="hero-halo"></div>
        <div className="hero-intro"><Document><Paragraph className="eyebrow" role="Eyebrow">ONE LANGUAGE / REAL BOUNDARIES</Paragraph></Document></div>
        <div className="hero-title text-document"><Document><Heading className="text-fit-target" role="HeroHeading">AI-native TypeScript for the next **ChatGPT**.</Heading></Document></div>
        <div className="hero-summary text-document"><Document><Paragraph role="Body">Copeland TS unifies **React**, .NET, npm, templates, and typed browser-to-CLR workflows—so AI writes less glue code and more product.</Paragraph></Document></div>
        <div className="hero-actions"><a href="#features">Explore the model</a><a href="#architecture">See architecture</a></div>
        <p className="hero-accent">components + streams + tables</p>
        <div className="code-badge text-document"><Document><CodeBlock language="ts">stream Copeland — featureGrid: FeatureCard(props); typed-browser-to-clr-contract-token-0123456789</CodeBlock></Document></div>
    </section>;
}

// The page positions one Hero host. This private stream owns the renderer
// attachment and captures the explicit profile locally.
function Hero(profile: int): ReactNode {
    stream Surface<0px, 0px> {
        width: fill;
        height: fill;
        content: HeroContent(profile) { width: fill; height: fill; }
    }

    return Surface();
}

function LanguageExample(): ReactNode {
    return <section className="language-example"><span>TypeScript</span><span className="arrow">→</span><span>TableScript</span><span className="arrow">→</span><span>TemplateScript</span></section>;
}

function Architecture(): ReactNode {
    return <section id="architecture" className="architecture"><Document><Paragraph className="eyebrow" role="Eyebrow">CANONICAL PIPELINE</Paragraph><Heading role="SectionHeading">Components execute. Layouts describe spatial relations.</Heading><List><Item><Paragraph>Components provide behavior and content.</Paragraph></Item><Item><Paragraph>Streams bind that content to named regions.</Paragraph></Item><Item><Paragraph>Compiler-projected tables produce **neutral** browser hosts.</Paragraph></Item></List></Document></section>;
}

function CallToAction(): ReactNode {
    return <section className="call-to-action"><Document><Paragraph className="eyebrow" role="Eyebrow">START WITH A REAL HOST</Paragraph><Heading role="SectionHeading">Build the bridge, then inspect the tables.</Heading></Document><div className="command-actions"><button onClick={() => Copy("dotnet new copeland-react")}>Copy starter command</button><p className="text-document">dotnet new copeland-react</p></div></section>;
}

function Footer(): ReactNode {
    return <footer className="site-footer"><Document><Paragraph role="Caption">Copeland TS / React / Machina / TSPack — read the [architecture guide](#architecture).</Paragraph></Document><a href="#overview">Back to overview ↑</a></footer>;
}

stream CopelandDesktop<0px, 0px> {
    width: 1440px;
    height: 900px;
    row root {
        commandBar: CommandBar() { width: 240px; height: fill; }
        overlay page { width: fill; height: fill; overflow: scrollY;
          column content { x: 0px; y: 0px; width: 1200px; height: 1012px;
            hero: Hero(3) { height: 362px; }
            languageExample: LanguageExample() { height: 56px; }
            grid featureGrid: [
                FeatureCard({ icon: "⌇⌁⌇", title: "Typed browser ↔ CLR", body: "End-to-end types across browser and .NET without a JSON tax." }),
                FeatureCard({ icon: "◉ ◈", title: "React without lock-in", body: "Use ordinary third-party React components beneath neutral hosts." }),
                FeatureCard({ icon: "▤ </>", title: "Static templates", body: "Bounded compile-time structure with no hidden second runtime." }),
                FeatureCard({ icon: "▦ ⎇", title: "Inspectable tables", body: "Layouts, bindings, and documents remain compiler-visible." })
            ] { columns: 4; gap: 16px; height: 260px; }
            architecture: Architecture() { height: 170px; }
            callToAction: CallToAction() { height: 100px; }
            footer: Footer() { height: 64px; }
          }
        }
    }
}

stream CopelandTablet<0px, 0px> {
    width: 768px;
    height: 1024px;
    column root {
        commandBar: CompactCommandBar() { height: 72px; }
        overlay page { height: fill; overflow: scrollY;
          column content { x: 0px; y: 0px; width: 768px; height: 1042px;
            hero: Hero(2) { height: 340px; }
            languageExample: LanguageExample() { height: 58px; }
            grid featureGrid: [
                FeatureCard({ icon: "⌇⌁⌇", title: "Typed browser ↔ CLR", body: "End-to-end types across browser and .NET without a JSON tax." }),
                FeatureCard({ icon: "◉ ◈", title: "React without lock-in", body: "Use ordinary third-party React components beneath neutral hosts." }),
                FeatureCard({ icon: "▤ </>", title: "Static templates", body: "Bounded compile-time structure with no hidden second runtime." }),
                FeatureCard({ icon: "▦ ⎇", title: "Inspectable tables", body: "Layouts, bindings, and documents remain compiler-visible." })
            ] { columns: 2; gap: 14px; height: 300px; }
            architecture: Architecture() { height: 180px; }
            callToAction: CallToAction() { height: 110px; }
            footer: Footer() { height: 54px; }
          }
        }
    }
}

stream CopelandMobile<0px, 0px> {
    width: 390px;
    height: 844px;
    column root {
        commandBar: CompactCommandBar() { height: 74px; }
        overlay page { height: fill; overflow: scrollY;
          column content { x: 0px; y: 0px; width: 390px; height: 1544px;
            hero: Hero(1) { height: 390px; }
            languageExample: LanguageExample() { height: 62px; }
            grid featureGrid: [
                FeatureCard({ icon: "⌇⌁⌇", title: "Typed browser ↔ CLR", body: "End-to-end types across browser and .NET without a JSON tax." }),
                FeatureCard({ icon: "◉ ◈", title: "React without lock-in", body: "Use ordinary third-party React components beneath neutral hosts." }),
                FeatureCard({ icon: "▤ </>", title: "Static templates", body: "Bounded compile-time structure with no hidden second runtime." }),
                FeatureCard({ icon: "▦ ⎇", title: "Inspectable tables", body: "Layouts, bindings, and documents remain compiler-visible." })
            ] { columns: 1; gap: 12px; height: 520px; }
            architecture: Architecture() { height: 250px; }
            callToAction: CallToAction() { height: 244px; }
            footer: Footer() { height: 78px; }
          }
        }
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
