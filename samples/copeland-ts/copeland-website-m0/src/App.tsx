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

// The plain-string convenience form normalizes to one Body paragraph.
function Text(value: string): ReactNode { return <p className="text-document text-body">{value}</p>; }

function HeroIntro(): ReactNode { return <div className="eyebrow hero-intro">{Text("ONE LANGUAGE / REAL BOUNDARIES")}</div>; }
function HeroTitle(): ReactNode {
    return <Text className="text-document"><Document><Heading className="text-fit-target" role="HeroHeading">AI-native TypeScript for the next **ChatGPT**.</Heading></Document></Text>;
}
function HeroSummary(): ReactNode {
    return <Text className="text-document hero-summary"><Document><Paragraph role="Body">Copeland TS unifies **React**, .NET, npm, templates, and typed browser-to-CLR workflows—so AI writes less glue code and more product.</Paragraph></Document></Text>;
}
function HeroActions(): ReactNode { return <div className="hero-actions"><a href="#features">Explore the model</a><a href="#architecture">See architecture</a></div>; }

function CodeBadge(): ReactNode {
    return <Text className="text-document code-badge"><Document><CodeBlock language="ts">stream Copeland — featureGrid: Features(); typed-browser-to-clr-contract-token-0123456789</CodeBlock></Document></Text>;
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

function BridgeCard(): ReactNode { return <article className="feature-card"><span>⌇⌁⌇</span><Document><Heading role="CardHeading">Typed browser ↔ CLR</Heading><Paragraph role="Body">End-to-end types across browser and .NET without a JSON tax.</Paragraph></Document></article>; }
function ReactCard(): ReactNode { return <article className="feature-card"><span>◉ ◈</span><Document><Heading role="CardHeading">React components</Heading><Paragraph role="Body">Use ordinary third-party **React components** beneath generated hosts.</Paragraph></Document></article>; }
function TemplatesCard(): ReactNode { return <article className="feature-card"><span>{"▤ </>"}</span><Document><Heading role="CardHeading">Static templates</Heading><Paragraph role="Body">Bounded compile-time structure with no hidden second runtime.</Paragraph></Document></article>; }
function TablesCard(): ReactNode { return <article className="feature-card"><span>▦ ⎇</span><Document><Heading role="CardHeading">Inspectable tables</Heading><Paragraph role="Body">Typed, diffable, **reviewable** layout facts stored as ordinary source.</Paragraph></Document></article>; }

function Architecture(): ReactNode {
    return <section id="architecture" className="architecture"><Document><Paragraph className="eyebrow" role="Eyebrow">CANONICAL PIPELINE</Paragraph><Heading role="SectionHeading">Components execute. Layouts describe spatial relations.</Heading><List><Item><Paragraph>Components provide behavior and content.</Paragraph></Item><Item><Paragraph>Streams bind that content to named regions.</Paragraph></Item><Item><Paragraph>Compiler-projected tables produce **neutral** browser hosts.</Paragraph></Item></List></Document></section>;
}

function CallToAction(): ReactNode {
    return <section className="call-to-action"><Document><Paragraph className="eyebrow" role="Eyebrow">START WITH A REAL HOST</Paragraph><Heading role="SectionHeading">Build the bridge, then inspect the tables.</Heading></Document><div className="command-actions"><button onClick={() => Copy("dotnet new copeland-react")}>Copy starter command</button><Text className="text-document"><Document><CodeBlock language="console">dotnet new copeland-react</CodeBlock></Document></Text></div></section>;
}

function Footer(): ReactNode {
    return <footer className="site-footer"><Document><Paragraph role="Caption">Copeland TS / React / Machina / TSPack — read the [architecture guide](#architecture).</Paragraph></Document><a href="#overview">Back to overview ↑</a></footer>;
}

// The three roots deliberately have independent topology. Their nested hosts
// are the page geometry; the components above do not receive layout classes.
stream CopelandDesktop<0px, 0px> {
    width: 1440px;
    height: 900px;
    row root {
        commandBar: CommandBar() { width: 240px; height: fill; }
        overlay page { width: fill; height: fill; overflow: scrollY;
          column content { x: 0px; y: 0px; width: 1200px; height: 1012px;
            overlay hero { height: 362px;
                heroHalo: HeroHalo() { } with expandFrom(heroTitle, 18px);
                heroIntro: HeroIntro() { x: 52px; y: 60px; width: 600px; height: 18px; }
                heroTitle: HeroTitle() { x: 52px; y: 84px; width: 600px; height: 104px; overflow: clip; fontSize: 54px; minFontSize: 42px; lines: 2; wrap: wrap; textFit: scaleDown; textFallback: ellipsis; }
                heroSummary: HeroSummary() { x: 52px; y: 198px; width: 600px; height: 52px; overflow: clip; }
                heroActions: HeroActions() { x: 52px; y: 266px; width: 360px; height: 40px; overflow: clip; }
                heroAccent: HeroAccent() { width: 280px; height: 28px; } with centerXIn(heroTitle);
                codeBadge: CodeBadge() { y: 106px; width: 300px; height: 118px; overflow: auto; } with placeRightOf(heroTitle, 32px);
            }
            languageExample: LanguageExample() { height: 56px; }
            grid featureGrid: [BridgeCard(), ReactCard(), TemplatesCard(), TablesCard()] { columns: 4; gap: 16px; height: 260px; }
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
          overlay hero { height: 340px;
            heroHalo: HeroHalo() { } with expandFrom(heroTitle, 16px);
            heroIntro: HeroIntro() { x: 42px; y: 44px; width: 480px; height: 18px; }
            heroTitle: HeroTitle() { x: 42px; y: 68px; width: 480px; height: 118px; overflow: clip; fontSize: 48px; minFontSize: 36px; lines: 3; wrap: wrap; textFit: scaleDown; textFallback: ellipsis; }
            heroSummary: HeroSummary() { x: 42px; y: 194px; width: 480px; height: 58px; overflow: clip; }
            heroActions: HeroActions() { x: 42px; y: 268px; width: 360px; height: 40px; overflow: clip; }
            heroAccent: HeroAccent() { width: 270px; height: 28px; } with centerXIn(heroTitle);
            codeBadge: CodeBadge() { x: 540px; y: 46px; width: 190px; height: 90px; overflow: auto; }
        }
        languageExample: LanguageExample() { height: 58px; }
        grid featureGrid: [BridgeCard(), ReactCard(), TemplatesCard(), TablesCard()] { columns: 2; gap: 14px; height: 300px; }
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
          overlay hero { height: 390px;
            heroHalo: HeroHalo() { } with expandFrom(heroTitle, 12px);
            heroIntro: HeroIntro() { x: 22px; y: 44px; width: 346px; height: 18px; }
            heroTitle: HeroTitle() { x: 22px; y: 68px; width: 346px; height: 112px; overflow: clip; fontSize: 40px; minFontSize: 30px; lines: 3; wrap: wrap; textFit: scaleDown; textFallback: ellipsis; }
            heroSummary: HeroSummary() { x: 22px; y: 190px; width: 346px; height: 62px; overflow: clip; }
            heroActions: HeroActions() { x: 22px; y: 266px; width: 330px; height: 36px; overflow: clip; }
            heroAccent: HeroAccent() { width: 250px; height: 28px; } with centerXIn(heroTitle);
            codeBadge: CodeBadge() { width: 346px; height: 48px; overflow: auto; } with centerXIn(hero) with placeBelow(heroActions, 14px);
        }
        languageExample: LanguageExample() { height: 62px; }
        grid featureGrid: [BridgeCard(), ReactCard(), TemplatesCard(), TablesCard()] { columns: 1; gap: 12px; height: 520px; }
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
