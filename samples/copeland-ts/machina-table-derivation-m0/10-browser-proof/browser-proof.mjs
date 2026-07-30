import { spawn } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const tspackPlaywright = process.env.TSPACK_PLAYWRIGHT_MODULE ?? "../../../../../tspack/node_modules/playwright";
const { chromium } = require(tspackPlaywright);

const baseUrl = "http://127.0.0.1:4176";
const tolerance = 0.01;
const artifactDirectory = "artifacts/cts-table-derivation-m0";
const server = spawn("node", ["server.mjs"], { stdio: ["ignore", "pipe", "pipe"] });
const diagnostics = { console: [], page: [], request: [] };
let browser;

try {
  await waitForServer();
  browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 800, height: 600 } });
  page.on("console", message => { if (message.type() === "error") diagnostics.console.push(message.text()); });
  page.on("pageerror", error => diagnostics.page.push(error.message));
  page.on("requestfailed", request => diagnostics.request.push({ url: request.url(), error: request.failure()?.errorText ?? "unknown" }));
  page.on("response", response => { if (response.status() >= 400) diagnostics.request.push({ url: response.url(), status: response.status() }); });
  await page.goto(baseUrl, { waitUntil: "networkidle" });

  const rectangles = await page.evaluate(() => {
    const names = ["root", "dialog", "tooltip", "backdrop"];
    const result = {};
    for (const name of names) {
      const element = document.querySelector(`[data-machina-box='${name}']`);
      if (!(element instanceof HTMLElement)) throw new Error(`Missing semantic host '${name}'.`);
      const rect = element.getBoundingClientRect();
      result[name] = { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height };
    }
    return result;
  });

  equal("dialog center x", rectangles.dialog.left + rectangles.dialog.width / 2, rectangles.root.left + rectangles.root.width / 2);
  equal("dialog center y", rectangles.dialog.top + rectangles.dialog.height / 2, rectangles.root.top + rectangles.root.height / 2);
  equal("tooltip bottom plus gap", rectangles.tooltip.bottom + 12, rectangles.dialog.top);
  equal("tooltip right", rectangles.tooltip.right, rectangles.dialog.right);
  equal("backdrop left", rectangles.backdrop.left, rectangles.dialog.left - 20);
  equal("backdrop top", rectangles.backdrop.top, rectangles.dialog.top - 20);
  equal("backdrop right", rectangles.backdrop.right, rectangles.dialog.right + 20);
  equal("backdrop bottom", rectangles.backdrop.bottom, rectangles.dialog.bottom + 20);
  if (diagnostics.console.length || diagnostics.page.length || diagnostics.request.length) throw new Error(`Browser diagnostics: ${JSON.stringify(diagnostics)}`);

  await mkdir(artifactDirectory, { recursive: true });
  await page.screenshot({ path: `${artifactDirectory}/relative-layout-proof.png` });
  await writeFile(`${artifactDirectory}/rectangle-report.json`, `${JSON.stringify({ success: true, tolerance, rectangles, diagnostics }, null, 2)}\n`);
  process.stdout.write(`${JSON.stringify({ success: true, tolerance, rectangles }, null, 2)}\n`);
} finally {
  await browser?.close();
  if (!server.killed) server.kill();
}

function equal(label, actual, expected) {
  if (Math.abs(actual - expected) > tolerance) throw new Error(`${label}: expected ${expected}, received ${actual}, tolerance ${tolerance}.`);
}

async function waitForServer() {
  for (let attempt = 0; attempt < 100; attempt += 1) {
    try {
      const response = await fetch(baseUrl);
      if (response.ok) return;
    } catch { }
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  throw new Error("Timed out waiting for the TSPack browser-proof server.");
}
