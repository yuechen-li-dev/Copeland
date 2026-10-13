import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { startServer } from "./server.ts";

const require = createRequire(new URL("../baseline/package.json", import.meta.url));
const { chromium } = require("playwright") as typeof import("../baseline/node_modules/playwright/index.js");
const websiteRoot = fileURLToPath(new URL("../../copeland-website-m0/", import.meta.url));
const stabilization = process.env.FRICTION_STABILIZATION === "1";
const evidenceName = process.env.FRICTION_EVIDENCE_NAME ?? (stabilization ? "react-ui-stabilization-m1" : "react-friction-m0");
let milestone = stabilization ? "CTS-REACT-UI-STABILIZATION-M1" : "CTS-REACT-FRICTION-M0";
if (evidenceName === "react-ui-grammar-m3") {
    milestone = "CTS-REACT-UI-GRAMMAR-M3";
}
if (evidenceName === "react-ui-layout-templates-m4") {
    milestone = "CTS-REACT-UI-LAYOUT-TEMPLATES-M4";
}
if (evidenceName === "react-ui-semantic-api-m5") {
    milestone = "CTS-REACT-UI-SEMANTIC-API-M5";
}
const outputRoot = fileURLToPath(new URL(stabilization ? "../artifacts/stabilization/screenshots/" : "../artifacts/screenshots/", import.meta.url));
const reportRoot = fileURLToPath(new URL(`../../../../docs/Copeland/reviews/evidence/${evidenceName}/`, import.meta.url));
const server = await startServer(0, `${websiteRoot}/dist/browser`, {
    "/text-fit.js": `${websiteRoot}/runtime/browser-v1.js`
});
let browser: import("../baseline/node_modules/playwright/index.js").Browser | undefined;
const diagnostics: string[] = [];
const observations: object[] = [];

try {
    browser = await chromium.launch({ channel: process.env.FRICTION_BROWSER_CHANNEL ?? "chromium" });
    await mkdir(outputRoot, { recursive: true });
    await mkdir(reportRoot, { recursive: true });
    for (const width of [1440, 768, 390]) {
        const page = await browser.newPage({ viewport: { width, height: 900 } });
        page.on("pageerror", error => diagnostics.push(error.message));
        page.on("console", message => {
            if (message.type() === "warning" || message.type() === "error") {
                diagnostics.push(message.text());
            }
        });
        page.on("requestfailed", request => diagnostics.push(`${request.url()}: ${request.failure()?.errorText}`));
        page.on("response", response => {
            if (response.status() >= 400) { diagnostics.push(`HTTP ${response.status()}: ${response.url()}`); }
        });
        await page.goto(server.url, { waitUntil: "networkidle" });
        await page.locator(".feature-card").last().waitFor();
        await page.evaluate(async () => { await document.fonts.ready; });
        const cards = await page.locator(".feature-card").evaluateAll(elements => elements.map(element => {
            const box = element.getBoundingClientRect();
            const paragraph = element.querySelector("p")!;
            const range = document.createRange();
            range.selectNodeContents(paragraph);
            const text = range.getBoundingClientRect();
            return {
                title: element.querySelector("h2")?.textContent,
                frameHeight: box.height,
                overflowPolicy: getComputedStyle(element).overflowY,
                contentBottom: text.bottom - box.top,
                hiddenTextPixels: Math.max(0, text.bottom - box.bottom),
                clipped: text.bottom > box.bottom + 1
            };
        }));
        assert.equal(cards.length, 4);
        if (stabilization) {
            assert.equal(cards.some(card => card.clipped), false, "Cards must not conceal paragraph content");
            for (const card of await page.locator(".feature-card").all()) {
                await card.scrollIntoViewIfNeeded();
                await card.focus();
                await page.keyboard.press("End");
                const reading = await card.evaluate(element => {
                    let scroller: HTMLElement | null = element.parentElement;
                    while (scroller && !["auto", "scroll"].includes(getComputedStyle(scroller).overflowY)) {
                        scroller = scroller.parentElement;
                    }
                    if (!scroller) { throw new Error("Missing declared card scroll surface"); }
                    const range = document.createRange();
                    range.selectNodeContents(element.querySelector("p")!);
                    const text = range.getBoundingClientRect();
                    const frame = scroller.getBoundingClientRect();
                    return {
                        title: element.querySelector("h2")?.textContent,
                        scrollTop: scroller.scrollTop,
                        scrollExtent: scroller.scrollHeight - scroller.clientHeight,
                        tailVisible: text.bottom <= frame.bottom + 1,
                        overflowY: getComputedStyle(scroller).overflowY
                    };
                });
                assert.equal(reading.tailVisible, true, "Keyboard must reach the complete paragraph");
                observations.push({ width, phase: "keyboard-readable-card", ...reading });
            }
        }
        observations.push({ width, phase: "original-authored-copy", cards });
        await page.locator('[data-machina-box="featureGrid"]').screenshot({ path: `${outputRoot}/original-website-${width}-cards.png` });
        process.stdout.write(`Original website ${width}: ${cards.filter(card => card.clipped).length}/4 cards clip prose.\n`);
        await page.close();
    }
    assert.deepEqual(diagnostics, []);
    const sourceHashes: Record<string, string> = {};
    for (const path of ["src/App.tsx", "styles.css", "tsconfig.tsx", "manifest.tsx", "dist/browser/src/App.js", "dist/browser/generated/layouts.css"]) {
        sourceHashes[path] = createHash("sha256").update(await readFile(`${websiteRoot}/${path}`)).digest("hex");
    }
    await writeFile(`${reportRoot}/website-regression.json`, JSON.stringify({
        milestone,
        source: "samples/copeland-ts/copeland-website-m0",
        build: "current TSPack managed target with repository Copeland.Cli",
        browser: { channel: process.env.FRICTION_BROWSER_CHANNEL ?? "chromium", version: browser.version() },
        sourceHashes, diagnostics, observations
    }, null, 2) + "\n");
} catch (error) {
    await writeFile(`${outputRoot}/website-failed-run.json`, JSON.stringify({ error: String(error), diagnostics, observations }, null, 2) + "\n");
    if (browser) {
        for (const context of browser.contexts()) {
            for (const page of context.pages()) {
                await page.screenshot({ path: `${outputRoot}/website-failed-page.png`, fullPage: true });
            }
        }
    }
    throw error;
} finally {
    await browser?.close();
    await server.close();
}
