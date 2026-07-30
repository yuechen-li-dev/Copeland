import { spawn } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const playwrightModule = process.env.TSPACK_PLAYWRIGHT_MODULE ?? "../../../../tspack/node_modules/playwright";
const { chromium } = require(playwrightModule);
const baseUrl = "http://127.0.0.1:4173";
const artifactDirectory = "artifacts/cts-website-table-layout-m0";
const tolerance = 0.01;
const server = spawn("node", ["server.mjs"], { stdio: ["ignore", "pipe", "pipe"] });
const diagnostics = { console: [], page: [], request: [] };
const evidence = [];
let browser;

try {
  await waitForServer();
  browser = await chromium.launch();

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

    const rectangles = await page.evaluate(layout => {
      const box = name => {
        const element = document.querySelector(`[data-machina-layout='${layout}'][data-machina-box='${name}']`);
        if (!(element instanceof HTMLElement)) throw new Error(`Missing ${layout}.${name}.`);
        const rect = element.getBoundingClientRect();
        return { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height };
      };
      const featureCards = [...document.querySelectorAll(".feature-card")].map(element => {
        const rect = element.getBoundingClientRect();
        return { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height };
      });
      return { root: box("root"), commandBar: box("commandBar"), hero: box("hero"), heroCopy: box("heroCopy"), heroAccent: box("heroAccent"), heroHalo: box("heroHalo"), codeBadge: box("codeBadge"), featureGrid: box("featureGrid"), footer: box("footer"), featureCards, scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth };
    }, profile.layout);

    equal(`${profile.name} root width`, rectangles.root.width, profile.width);
    if (profile.name !== "mobile") equal(`${profile.name} root height`, rectangles.root.height, profile.height);
    if (rectangles.scrollWidth > rectangles.clientWidth) throw new Error(`${profile.name} page has horizontal overflow: ${rectangles.scrollWidth} > ${rectangles.clientWidth}.`);
    if (rectangles.footer.bottom > rectangles.root.bottom + tolerance) throw new Error(`${profile.name} footer leaves its root.`);
    if (rectangles.featureCards.length !== 4) throw new Error(`${profile.name} expected four bounded feature cards.`);
    for (const [index, card] of rectangles.featureCards.entries()) {
      if (card.left < rectangles.featureGrid.left - tolerance || card.right > rectangles.featureGrid.right + tolerance || card.top < rectangles.featureGrid.top - tolerance || card.bottom > rectangles.featureGrid.bottom + tolerance) {
        throw new Error(`${profile.name} feature card ${index} escapes featureGrid.`);
      }
    }
    equal(`${profile.name} accent center`, rectangles.heroAccent.left + rectangles.heroAccent.width / 2, rectangles.heroCopy.left + rectangles.heroCopy.width / 2, rectangles);
    equal(`${profile.name} halo left`, rectangles.heroHalo.left, rectangles.heroCopy.left - (profile.name === "desktop" ? 18 : profile.name === "tablet" ? 16 : 12));
    equal(`${profile.name} halo right`, rectangles.heroHalo.right, rectangles.heroCopy.right + (profile.name === "desktop" ? 18 : profile.name === "tablet" ? 16 : 12));

    if (profile.name === "desktop") {
      equal("desktop command bar width", rectangles.commandBar.width, 240);
      equal("desktop code badge adjacency", rectangles.codeBadge.left, rectangles.heroCopy.right + 32);
      if (rectangles.codeBadge.left < rectangles.heroCopy.right) throw new Error("desktop code badge overlaps hero copy.");
    }

    if (profile.name === "mobile") {
      equal("mobile code badge adjacency", rectangles.codeBadge.top, rectangles.heroCopy.bottom + 8);
    }

    await mkdir(artifactDirectory, { recursive: true });
    await page.screenshot({ path: `${artifactDirectory}/rectangles-${profile.name}.png` });
    evidence.push({ profile, rectangles });
    await page.close();
  }

  if (diagnostics.console.length || diagnostics.page.length || diagnostics.request.length) throw new Error(`Browser diagnostics: ${JSON.stringify(diagnostics)}`);
  await writeFile(`${artifactDirectory}/rectangle-report.json`, `${JSON.stringify({ success: true, tolerance, diagnostics, evidence }, null, 2)}\n`);
  process.stdout.write(`${JSON.stringify({ success: true, profiles: evidence.map(item => item.profile.name) }, null, 2)}\n`);
} finally {
  await browser?.close();
  if (!server.killed) server.kill();
}

function equal(label, actual, expected, context) {
  if (Math.abs(actual - expected) > tolerance) throw new Error(`${label}: expected ${expected}, received ${actual}, tolerance ${tolerance}. ${context ? JSON.stringify(context) : ""}`);
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
