import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { startServer } from "./server.ts";

const require = createRequire(new URL("../baseline/package.json", import.meta.url));
const { chromium } = require("playwright") as typeof import("../baseline/node_modules/playwright/index.js");
type Page = import("../baseline/node_modules/playwright/index.js").Page;
const root = fileURLToPath(new URL("../", import.meta.url));
const stabilization = process.env.FRICTION_STABILIZATION === "1";
const evidenceName = stabilization ? "react-ui-stabilization-m1" : "react-friction-m0";
const evidenceDirectory = fileURLToPath(new URL(`../../../../docs/Copeland/reviews/evidence/${evidenceName}/`, import.meta.url));
const screenshotDirectory = stabilization ? `${root}/artifacts/stabilization/screenshots` : `${root}/artifacts/screenshots`;
const server = await startServer();
let browser: import("../baseline/node_modules/playwright/index.js").Browser | undefined;
const diagnostics: string[] = [];
const observations: Record<string, unknown>[] = [];
const parity: Record<string, string> = {};

async function settle(page: Page) {
    await page.evaluate(async () => {
        await document.fonts.ready;
        await new Promise<void>(done => requestAnimationFrame(() => requestAnimationFrame(() => done())));
    });
}

async function snapshot(page: Page) {
    return page.evaluate(() => {
        function rectangle(element: Element) {
            const box = element.getBoundingClientRect();
            return { x: box.x, y: box.y, width: box.width, height: box.height, bottom: box.bottom, right: box.right };
        }
        const body = document.querySelector("#article-body");
        const notes = document.querySelector("#article-notes");
        const list = document.querySelector('[aria-label="Issue list"]');
        const detail = document.querySelector("#issue-detail");
        const articleHeading = document.querySelector("header.panel");
        let articleFlow = articleHeading?.parentElement ?? null;
        while (articleFlow) {
            const style = getComputedStyle(articleFlow);
            if (style.display === "flex" && style.flexDirection === "column") {
                break;
            }
            articleFlow = articleFlow.parentElement;
        }
        const prose = Array.from(document.querySelectorAll(".readable, .token, h1, h2, [role='alert']")).map(element => {
            const range = document.createRange();
            range.selectNodeContents(element);
            const text = range.getBoundingClientRect();
            const box = element.getBoundingClientRect();
            let clipped = false;
            for (let ancestor: Element | null = element; ancestor; ancestor = ancestor.parentElement) {
                const style = getComputedStyle(ancestor);
                const frame = ancestor.getBoundingClientRect();
                const boundedY = style.overflowY === "hidden" || style.overflowY === "clip";
                const boundedX = style.overflowX === "hidden" || style.overflowX === "clip";
                if ((boundedY && text.bottom > frame.bottom + 1) || (boundedX && text.right > frame.right + 1)) {
                    clipped = true;
                }
            }
            return { text: element.textContent, widthOverflow: text.right > box.right + 1, clipped };
        });
        const code = document.querySelector("pre");
        return {
            horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
            prose,
            article: body ? rectangle(body) : null,
            articleHost: body?.parentElement ? rectangle(body.parentElement) : null,
            notes: notes ? rectangle(notes) : null,
            list: list ? rectangle(list) : null,
            detail: detail ? rectangle(detail) : null,
            articleGap: body && articleHeading ? body.getBoundingClientRect().top - articleHeading.getBoundingClientRect().bottom : null,
            articlePadding: articleFlow ? getComputedStyle(articleFlow).paddingLeft : null,
            codeScroll: code ? { scrollWidth: code.scrollWidth, clientWidth: code.clientWidth } : null
        };
    });
}

async function record(page: Page, implementation: string, scenario: string, width: number, readingMustFit = true) {
    await settle(page);
    const facts = await snapshot(page);
    if (readingMustFit) {
        assert.equal(facts.horizontalOverflow, false, `${implementation}/${scenario}: horizontal overflow`);
        assert.equal(facts.prose.some(item => item.clipped || item.widthOverflow), false, `${implementation}/${scenario}: unreadable prose`);
        if (facts.article && facts.notes) {
            assert.ok(facts.notes.y >= facts.article.bottom - 1, `${implementation}/${scenario}: prose overlaps following notes`);
        }
    }
    observations.push({ implementation, scenario, width, ...facts });
    process.stdout.write(`${implementation}/${width}/${scenario}\n`);
    await page.screenshot({ path: `${screenshotDirectory}/${implementation}-${width}-${scenario}.png`, fullPage: true });
    return facts;
}

try {
    browser = await chromium.launch({ channel: process.env.FRICTION_BROWSER_CHANNEL ?? "chromium" });
    await mkdir(evidenceDirectory, { recursive: true });
    await mkdir(screenshotDirectory, { recursive: true });
    for (const implementation of ["copeland", "typescript"]) {
        for (const width of [1280, 768, 390]) {
            const page = await browser.newPage({ viewport: { width, height: 900 } });
            page.on("console", message => {
                if (message.type() === "warning" || message.type() === "error") {
                    diagnostics.push(`${implementation}: ${message.type()}: ${message.text()}`);
                }
            });
            page.on("pageerror", error => diagnostics.push(`${implementation}: ${error.message}`));
            page.on("requestfailed", request => diagnostics.push(`${implementation}: ${request.url()}: ${request.failure()?.errorText}`));
            page.on("response", response => {
                if (response.status() >= 400) { diagnostics.push(`${implementation}: HTTP ${response.status()}: ${response.url()}`); }
            });
            const path = implementation === "copeland" ? "/" : "/baseline/";
            await page.goto(server.url + path, { waitUntil: "networkidle" });
            await page.getByRole("heading", { name: "A field guide to dependable interfaces" }).waitFor();
            const copy = await page.locator('[data-prose="article"]').innerText();
            parity[`${implementation}-short`] = copy;
            await record(page, implementation, "article-short", width);
            if (stabilization) {
                const article = await snapshot(page);
                assert.equal(article.articleGap, 12, "Declared article gap must be realized");
                assert.equal(article.articlePadding, "16px", "Declared article padding must be realized");
            }
            await page.getByRole("button", { name: "Change copy length", exact: true }).click();
            await page.getByText("This final sentence must remain reachable.", { exact: false }).waitFor();
            parity[`${implementation}-long`] = await page.locator('[data-prose="article"]').innerText();
            await record(page, implementation, "article-long", width);
            await page.evaluate(() => { document.documentElement.style.fontSize = "32px"; });
            await record(page, implementation, "article-text-200-percent", width);
            await page.evaluate(() => {
                document.documentElement.style.fontSize = "";
                document.body.style.fontFamily = "monospace";
            });
            await record(page, implementation, "article-fallback-font", width);
            await page.evaluate(() => { document.body.style.fontFamily = ""; });

            await page.getByRole("button", { name: "Issues", exact: true }).click();
            await page.locator('[data-issue="2"]').click();
            await page.getByRole("button", { name: "Insert issue", exact: true }).click();
            assert.equal(await page.locator("[data-issue]").count(), 4);
            assert.match(await page.locator("#issue-detail").innerText(), /SELECTED ISSUE\s*2/);
            const wide = await record(page, implementation, "issues-wide", width);
            if (stabilization && implementation === "copeland" && width >= 600) {
                assert.equal(wide.list?.width, 300, "Declared sidebar width must not shrink");
            }
            if (stabilization && width < 600) {
                assert.ok(wide.detail && wide.list && wide.detail.y >= wide.list.bottom - 1,
                    "Narrow profile must select the compact reading arrangement");
            }
            observations.push({ implementation, scenario: "requested-sidebar-width", width, declared: 300, actual: wide.list?.width });
            if (stabilization && width === 1280) {
                await page.setViewportSize({ width: 390, height: 900 });
                await settle(page);
                const compact = await snapshot(page);
                assert.ok(compact.detail && compact.list && compact.detail.y >= compact.list.bottom - 1,
                    "Resize must select the compact layout");
                assert.match(await page.locator("#issue-detail").innerText(), /SELECTED ISSUE\s*2/);
                await page.setViewportSize({ width, height: 900 });
                await settle(page);
                const restored = await snapshot(page);
                assert.equal(restored.list?.width, 300, "Resize must restore the fixed wide track");
                assert.match(await page.locator("#issue-detail").innerText(), /SELECTED ISSUE\s*2/);
                observations.push({ implementation, scenario: "resize-preserves-selection", from: width, through: 390, restored: width });
            }
            await page.getByRole("button", { name: "Change arrangement", exact: true }).click();
            assert.match(await page.locator("#issue-detail").innerText(), /SELECTED ISSUE\s*2/);
            await record(page, implementation, "issues-changed-arrangement", width);
            await page.getByLabel("Search issue titles").fill("Keyboard");
            await page.getByRole("button", { name: "Search", exact: true }).click();
            assert.equal(await page.locator("[data-issue]").count(), 1);
            assert.equal(await page.locator('[data-issue="2"]').count(), 1);
            await page.getByLabel("Search issue titles").fill("missing");
            await page.getByRole("button", { name: "Search", exact: true }).click();
            await page.getByText("No matching issues.", { exact: true }).waitFor();
            await page.getByRole("button", { name: "Show loading", exact: true }).click();
            await page.getByText("Loading issues…", { exact: true }).waitFor();
            await page.getByRole("button", { name: "Show error", exact: true }).click();
            await page.getByRole("button", { name: "Retry", exact: true }).click();
            await page.getByText("No matching issues.", { exact: true }).waitFor();
            await page.getByLabel("Search issue titles").fill("");
            await page.getByRole("button", { name: "Search", exact: true }).click();
            assert.equal(await page.locator("[data-issue]").count(), 4);
            observations.push({ implementation, scenario: "issue-interactions", width, search: true, empty: true, loading: true, retry: true, selectionRetained: true, insertion: true });

            await page.getByRole("button", { name: "Settings", exact: true }).click();
            await page.getByRole("button", { name: "Save workspace", exact: true }).click();
            await page.getByRole("alert").waitFor();
            await record(page, implementation, "settings-validation", width);
            await page.getByLabel("Workspace name").fill("Field station");
            await page.getByRole("button", { name: "Save workspace", exact: true }).click();
            await page.getByText("Saved workspace: Field station", { exact: true }).waitFor();
            await page.getByRole("button", { name: "Review settings", exact: true }).click();
            await page.getByRole("dialog").waitFor();
            assert.equal(await page.getByRole("button", { name: "Close review" }).evaluate(element => element === document.activeElement), true);
            await page.keyboard.press("Escape");
            await page.getByRole("dialog").waitFor({ state: "hidden" });
            assert.equal(await page.getByRole("button", { name: "Review settings" }).evaluate(element => element === document.activeElement), true);
            await page.getByRole("button", { name: "Review settings", exact: true }).click();
            await page.getByRole("button", { name: "Close review", exact: true }).click();
            await page.getByRole("dialog").waitFor({ state: "hidden" });
            observations.push({ implementation, scenario: "settings-interactions", width, validation: true, controlledInput: true, save: true, dialog: true, escape: true, reopen: true, focusReturn: true });

            await page.getByRole("button", { name: "Overflow", exact: true }).click();
            const overflow = await record(page, implementation, "assigned-height-overflow", width, false);
            assert.ok(overflow.article && overflow.notes && overflow.article.bottom > overflow.notes.y + 1, "Expected assigned-height overlap reproduction");
            observations.push({ implementation, scenario: "known-failure", width, cause: "180px assigned prose height", contentBottom: overflow.article.bottom, followingTop: overflow.notes.y, overlap: overflow.article.bottom - overflow.notes.y });
            await page.close();
        }
    }
    assert.equal(parity["copeland-short"], parity["typescript-short"]);
    assert.equal(parity["copeland-long"], parity["typescript-long"]);
    assert.deepEqual(diagnostics, []);
    const sourceHashes: Record<string, string> = {};
    for (const path of ["src/App.tsx", "src/State.ts", "src/Main.ts", "src/Content.ts", "baseline/App.tsx", "styles.css", "manifest.tsx", "tsconfig.tsx", "ts-lock.toml", "dist/browser/main.js", "dist/browser/generated/layouts.css", "dist/baseline/app.js"]) {
        sourceHashes[path] = createHash("sha256").update(await readFile(`${root}/${path}`)).digest("hex");
    }
    const report = {
        milestone: stabilization ? "CTS-REACT-UI-STABILIZATION-M1" : "CTS-REACT-FRICTION-M0",
        status: "qualified-corpus-with-known-failures",
        browser: { channel: process.env.FRICTION_BROWSER_CHANNEL ?? "chromium", version: browser.version() },
        diagnostics, sourceHashes, parity, observations
    };
    await writeFile(`${evidenceDirectory}/browser-report.json`, JSON.stringify(report, null, 2) + "\n");
    process.stdout.write(`Qualified ${observations.length} observations; paired content matches; diagnostics clean.\n`);
} catch (error) {
    await mkdir(`${root}/artifacts`, { recursive: true });
    await writeFile(`${root}/artifacts/failed-run.json`, JSON.stringify({
        error: String(error), diagnostics, observations
    }, null, 2) + "\n");
    if (browser) {
        for (const context of browser.contexts()) {
            for (const page of context.pages()) {
                await page.screenshot({ path: `${root}/artifacts/failed-page.png`, fullPage: true });
            }
        }
    }
    throw error;
} finally {
    await browser?.close();
    await server.close();
}
