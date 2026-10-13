import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { startServer } from "./server.ts";

type ProbeName = "Valid" | "Duplicate" | "InvalidIdentity" | "Failure";
interface ProbeState {
    storage: number[];
    render(name: ProbeName): void;
}
type ProbeGlobal = typeof globalThis & { __semanticProbe: ProbeState };
type ProbeModule = Record<ProbeName, (storage: number[]) => unknown>;
interface RootModule {
    createRoot(host: Element): { render(node: unknown): void };
}

const require = createRequire(new URL("../baseline/package.json", import.meta.url));
const { chromium } = require("playwright") as typeof import("../baseline/node_modules/playwright/index.js");
const root = fileURLToPath(new URL("../", import.meta.url));
const evidenceName = process.env.FRICTION_EVIDENCE_NAME ?? "react-ui-semantic-api-m5";
const evidenceRoot = fileURLToPath(new URL(`../../../../docs/Copeland/reviews/evidence/${evidenceName}/`, import.meta.url));
const report = JSON.parse(await readFile(`${evidenceRoot}/compiler-report.json`, "utf8"));
const specimen = report.results.find((result: { name: string }) => result.name === "semantic-library-projection");
assert.equal(specimen?.expectationMatched, true);
const probeRoot = `${root}/artifacts/${evidenceName}/probes`;
await mkdir(probeRoot, { recursive: true });
const assets: Record<string, string> = {};
const emittedHashes: Record<string, string> = {};
for (const [name, source] of Object.entries(specimen.javascript as Record<string, string>)) {
    const path = `${probeRoot}/${name}`;
    await writeFile(path, source);
    assets[`/probes/${name}`] = path;
    emittedHashes[name] = createHash("sha256").update(source).digest("hex");
}
const server = await startServer(0, undefined, assets);
let browser: import("../baseline/node_modules/playwright/index.js").Browser | undefined;
const diagnostics: string[] = [];
const observations: string[] = [];
try {
    browser = await chromium.launch({ channel: process.env.FRICTION_BROWSER_CHANNEL ?? "msedge", headless: true });
    const page = await browser.newPage();
    page.on("pageerror", error => diagnostics.push(error.stack ?? error.message));
    page.on("console", message => {
        if (message.type() === "error" || message.type() === "warning") {
            diagnostics.push(message.text());
        }
    });
    await page.goto(server.url);
    await page.locator("nav").waitFor();
    await page.evaluate(async () => {
        // Dynamic imports resolve through the same generated import map as the application.
        const importModule = (specifier: string): Promise<unknown> => import(specifier);
        const ReactDOM = await importModule("react-dom/client") as RootModule;
        const probe = await importModule("/probes/Probe.js") as ProbeModule;
        const host = document.createElement("div");
        host.id = "semantic-probe";
        document.body.appendChild(host);
        const storage = [0, 0];
        const renderRoot = ReactDOM.createRoot(host);
        const state: ProbeState = { storage, render: (name: ProbeName) => renderRoot.render(probe[name](storage)) };
        (globalThis as ProbeGlobal).__semanticProbe = state;
        state.render("Valid");
    });
    const host = page.locator("#semantic-probe");
    const storage = () => page.evaluate(() => [...(globalThis as ProbeGlobal).__semanticProbe.storage]);
    const render = async (name: ProbeName) => {
        await page.evaluate(name => (globalThis as ProbeGlobal).__semanticProbe.render(name), name);
    };
    await host.getByRole("button", { name: "Second reading" }).waitFor();
    assert.deepEqual(await storage(), [0, 0]);
    await host.getByRole("button", { name: "Second reading" }).click();
    assert.deepEqual(await storage(), [42, 1]);
    observations.push("Construction does not dispatch; activation converts and sends exactly once.");
    await render("Duplicate");
    await host.getByRole("alert").waitFor();
    assert.equal(await host.getByRole("alert").textContent(), "Choice identities must be unique.");
    assert.equal(await host.getByRole("button").count(), 0);
    observations.push("Duplicate identities are rejected before React reconciliation.");
    await render("InvalidIdentity");
    await host.getByText("Choice identities must be nonempty.", { exact: true }).waitFor();
    assert.equal(await host.getByRole("button").count(), 0);
    observations.push("Empty identities are rejected without a positional fallback.");
    await render("Failure");
    await host.getByRole("button", { name: "Try again" }).waitFor();
    assert.deepEqual(await storage(), [42, 1]);
    assert.equal(await host.getByRole("alert").getByText("Reading unavailable.").count(), 1);
    await host.getByRole("button", { name: "Try again" }).click();
    assert.deepEqual(await storage(), [99, 1]);
    observations.push("Optional action and cross-module enum event dispatch work through explicit capture.");
    assert.deepEqual(diagnostics, []);
    await writeFile(`${evidenceRoot}/semantic-library-report.json`, JSON.stringify({
        milestone: "CTS-REACT-UI-SEMANTIC-API-M5", browser: browser.version(),
        sourceHashes: specimen.sourceHashes, emittedHashes, observations, diagnostics
    }, null, 2) + "\n");
    process.stdout.write(`Semantic library: ${observations.length} browser assertions passed.\n`);
} finally {
    await browser?.close();
    await server.close();
}
