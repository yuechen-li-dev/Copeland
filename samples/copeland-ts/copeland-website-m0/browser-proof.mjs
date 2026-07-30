import { spawn } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const playwrightModule = process.env.TSPACK_PLAYWRIGHT_MODULE ?? "../../../../tspack/node_modules/playwright";
const { chromium } = require(playwrightModule);
const baseUrl = "http://127.0.0.1:4173";
const artifactDirectory = "artifacts/cts-web-content-fit-m0";
const tolerance = 1;
const server = spawn("node", ["server.mjs"], { stdio: ["ignore", "pipe", "pipe"] });
const diagnostics = { console: [], page: [], request: [] };
const evidence = [];
let browser;

try {
  await waitForServer();
  browser = await chromium.launch();
  await mkdir(artifactDirectory, { recursive: true });

  for (const profile of [
    { name: "desktop", layout: "CopelandDesktop", width: 1440, height: 900 },
    { name: "tablet", layout: "CopelandTablet", width: 768, height: 1024 },
    { name: "mobile", layout: "CopelandMobile", width: 390, height: 844 }
  ]) {
    const page = await browser.newPage({ viewport: { width: profile.width, height: profile.height } });
    page.on("console", message => { if (message.type() === "error") diagnostics.console.push(message.text()); });
    page.on("pageerror", error => diagnostics.page.push(error.message));
    page.on("requestfailed", request => diagnostics.request.push({ url: request.url(), error: request.failure()?.errorText ?? "unknown" }));
    page.on("response", response => { if (response.status() >= 400) diagnostics.request.push({ url: response.url(), status: response.status() }); });
    await page.goto(baseUrl, { waitUntil: "networkidle" });
    await page.waitForFunction(layout => document.querySelector(`[data-machina-layout='${layout}'][data-machina-box='heroTitle']`)?.dataset.machinaTextFit !== undefined, profile.layout);

    const initial = await page.evaluate(({ layout, source }) => (0, eval)(`(${source})`)(layout), { layout: profile.layout, source: snapshot.toString() });
    equal(`${profile.name} root width`, initial.root.width, profile.width, initial);
    equal(`${profile.name} root height`, initial.root.height, profile.height, initial);
    if (initial.document.scrollWidth > initial.document.clientWidth + tolerance) throw new Error(`${profile.name} page has horizontal overflow.`);
    if (initial.page.scrollHeight <= initial.page.clientHeight + tolerance) throw new Error(`${profile.name} page does not expose explicit vertical scrolling.`);
    assertInside(`${profile.name} title target`, initial.heroTitleTarget, initial.heroTitle);
    assertInside(`${profile.name} actions`, initial.heroActionsContent, initial.heroActions);
    if (initial.heroTitle.bottom > initial.heroActions.top + tolerance) throw new Error(`${profile.name} title overlaps the action region.`);
    if (initial.textFit.status !== "fit" && initial.textFit.status !== "fallback" && initial.textFit.status !== "minimum-overflow") throw new Error(`${profile.name} has no deterministic text-fit status.`);
    if (initial.textFit.size + tolerance < initial.textFit.minimum) throw new Error(`${profile.name} fitted below its authored minimum.`);
    if (initial.heroTitleTarget.scrollWidth > initial.heroTitleTarget.clientWidth + tolerance) throw new Error(`${profile.name} title is horizontally clipped.`);
    if (initial.codeBadge.scrollWidth <= initial.codeBadge.clientWidth + tolerance) throw new Error(`${profile.name} code region did not retain its intentional horizontal scroll extent.`);
    if (!initial.semantics.heroHeading || !initial.semantics.strong || !initial.semantics.link || !initial.semantics.list || !initial.semantics.code) throw new Error(`${profile.name} text document semantic DOM is incomplete.`);
    await page.screenshot({ path: `${artifactDirectory}/${profile.name}-initial.png` });

    const locality = [];
    for (const fixture of [
      { name: "short", text: "Copeland TS." },
      { name: "long", text: "AI-native TypeScript that gives product teams one inspectable language for browser, CLR, package, template, and table boundaries." },
      { name: "token", text: "copelandcontentfitunbrokencontracttoken0123456789012345678901234567890123456789" },
      { name: "minimum-fallback", text: "copelandcontentfitminimumfallbacktoken012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" }
    ]) {
      await page.evaluate(({ layout, text }) => {
        const target = document.querySelector(`[data-machina-layout='${layout}'][data-machina-box='heroTitle'] .text-fit-target`);
        if (!(target instanceof HTMLElement)) throw new Error("Missing text target.");
        target.textContent = text;
      }, { layout: profile.layout, text: fixture.text });
      await page.waitForTimeout(50);
      const changed = await page.evaluate(({ layout, source }) => (0, eval)(`(${source})`)(layout), { layout: profile.layout, source: snapshot.toString() });
      equal(`${profile.name} ${fixture.name} title box width`, changed.heroTitle.width, initial.heroTitle.width, changed);
      equal(`${profile.name} ${fixture.name} action box top`, changed.heroActions.top, initial.heroActions.top, changed);
      equal(`${profile.name} ${fixture.name} feature origin`, changed.featureGrid.top, initial.featureGrid.top, changed);
      if (changed.document.scrollWidth > changed.document.clientWidth + tolerance) throw new Error(`${profile.name} ${fixture.name} caused horizontal overflow.`);
      if (fixture.name === "minimum-fallback" && changed.textFit.status !== "fallback") throw new Error(`${profile.name} minimum fallback was not reported.`);
      locality.push({ fixture: fixture.name, textFit: changed.textFit, heroTitle: changed.heroTitle, heroActions: changed.heroActions, featureGrid: changed.featureGrid });
    }

    await page.evaluate(layout => {
      const surface = document.querySelector(`[data-machina-layout='${layout}'][data-machina-box='page']`);
      if (!(surface instanceof HTMLElement)) throw new Error("Missing page scroll surface.");
      surface.scrollTop = surface.scrollHeight;
    }, profile.layout);
    await page.waitForTimeout(50);
    const scrolled = await page.evaluate(({ layout, source }) => (0, eval)(`(${source})`)(layout), { layout: profile.layout, source: snapshot.toString() });
    if (scrolled.page.scrollTop <= 0) throw new Error(`${profile.name} page did not move when scrolled.`);
    assertInside(`${profile.name} footer after scroll`, scrolled.footer, scrolled.page);
    await page.screenshot({ path: `${artifactDirectory}/${profile.name}-footer.png` });
    evidence.push({ profile, initial, locality, scrolled });
    await page.close();
  }

  if (diagnostics.console.length || diagnostics.page.length || diagnostics.request.length) throw new Error(`Browser diagnostics: ${JSON.stringify(diagnostics)}`);
  await writeFile(`${artifactDirectory}/report.json`, `${JSON.stringify({ success: true, tolerance, diagnostics, evidence }, null, 2)}\n`);
  process.stdout.write(`${JSON.stringify({ success: true, profiles: evidence.map(item => item.profile.name) }, null, 2)}\n`);
} finally {
  await browser?.close();
  if (!server.killed) server.kill();
}

function snapshot(layout) {
  const requireElement = selector => {
    const element = document.querySelector(selector);
    if (!(element instanceof HTMLElement)) throw new Error(`Missing element ${selector}.`);
    return element;
  };
  const rectangle = element => {
    const rect = element.getBoundingClientRect();
    return { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height };
  };
  const box = name => rectangle(requireElement(`[data-machina-layout='${layout}'][data-machina-box='${name}']`));
  const heroTitleHost = requireElement(`[data-machina-layout='${layout}'][data-machina-box='heroTitle']`);
  const heroActionsHost = requireElement(`[data-machina-layout='${layout}'][data-machina-box='heroActions']`);
  const titleTarget = requireElement(`[data-machina-layout='${layout}'][data-machina-box='heroTitle'] .text-fit-target`);
  const actionsContent = requireElement(`[data-machina-layout='${layout}'][data-machina-box='heroActions'] .hero-actions`);
  const page = requireElement(`[data-machina-layout='${layout}'][data-machina-box='page']`);
  const code = requireElement(`[data-machina-layout='${layout}'][data-machina-box='codeBadge']`);
  const heroDocument = requireElement(`[data-machina-layout='${layout}'][data-machina-box='heroTitle'] .text-document`);
  const architecture = requireElement(`[data-machina-layout='${layout}'][data-machina-box='architecture']`);
  const footer = requireElement(`[data-machina-layout='${layout}'][data-machina-box='footer']`);
  return {
    root: box("root"), page: { ...rectangle(page), scrollHeight: page.scrollHeight, clientHeight: page.clientHeight, scrollTop: page.scrollTop }, content: box("content"), hero: box("hero"), heroTitle: rectangle(heroTitleHost), heroActions: rectangle(heroActionsHost), featureGrid: box("featureGrid"), heroTitleTarget: { ...rectangle(titleTarget), scrollWidth: titleTarget.scrollWidth, clientWidth: titleTarget.clientWidth }, heroActionsContent: rectangle(actionsContent), codeBadge: { ...rectangle(code), scrollWidth: code.scrollWidth, clientWidth: code.clientWidth }, footer: box("footer"), document: { scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }, textFit: { status: heroTitleHost.dataset.machinaTextFit, size: Number(heroTitleHost.dataset.machinaTextSize), minimum: Number(heroTitleHost.dataset.machinaTextMinimumSize) }, semantics: { heroHeading: heroDocument.querySelector("h1") !== null, strong: document.querySelector("strong") !== null, link: footer.querySelector("a[href='#architecture']") !== null, list: architecture.querySelector("ul > li") !== null, code: code.querySelector("pre") !== null }
  };
}

function assertInside(label, inner, outer) {
  if (inner.left < outer.left - tolerance || inner.right > outer.right + tolerance || inner.top < outer.top - tolerance || inner.bottom > outer.bottom + tolerance) throw new Error(`${label} escapes its assigned box: ${JSON.stringify({ inner, outer })}`);
}

function equal(label, actual, expected, context) {
  if (Math.abs(actual - expected) > tolerance) throw new Error(`${label}: expected ${expected}, received ${actual}; ${JSON.stringify(context)}`);
}

async function waitForServer() {
  for (let attempt = 0; attempt < 100; attempt += 1) {
    try {
      const response = await fetch(baseUrl);
      if (response.ok) return;
    } catch { }
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  throw new Error("Timed out waiting for the Copeland website host.");
}
