import assert from "node:assert/strict";
import { mkdir, writeFile, readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { chromium, type Page } from "playwright";
import { startServer } from "./server.ts";

interface Frame {
    identity: string;
    endpoints: Record<string, unknown>;
    getSnapshot(): { presentation: { props?: { "aria-label"?: string } }; revision: number };
    trace: { kind: string; revision: number }[];
    dispatch(event: string, payload: unknown[]): void;
}
interface Registry {
    instances: Map<string, Frame>;
    diagnostics: { id: string; message: string }[];
}
interface Observation {
    kind: string;
    value: string;
    id?: string;
}
const evidence = fileURLToPath(new URL("../../../../docs/Copeland/reviews/evidence/vue-friction-m8/", import.meta.url));
await mkdir(evidence, { recursive: true });
const server = await startServer();
const browser = await chromium.launch({ channel: process.env.FRICTION_BROWSER_CHANNEL ?? "msedge" });
const errors: string[] = [];
const observations: Record<string, unknown>[] = [];
const baselineFacts = new Map<string, unknown>();

async function compareDomain(page: Page, path: string, width: number, scenario: string): Promise<void> {
    const facts = {
        titles: (await page.locator("[data-choice]").allTextContents()).map(title => title.trim()),
        selected: await page.locator('[data-choice][aria-pressed="true"]').getAttribute("data-choice"),
        description: await page.locator('[data-prose="detail"]').textContent(),
    };
    const key = `${width}/${scenario}`;
    if (path === "/baseline/") baselineFacts.set(key, facts);
    else assert.deepEqual(facts, baselineFacts.get(key), `${path}/${key}: Vue baseline parity`);
    observations.push({ name: `${path}/${key}/domain-parity`, ...facts });
}

function monitor(page: Page): void {
    page.on("pageerror", error => errors.push(error.stack ?? error.message));
    page.on("console", message => {
        if (message.type() === "warning" || message.type() === "error") errors.push(message.text());
    });
    page.on("response", response => {
        if (response.status() >= 400) errors.push(`${response.status()}: ${response.url()}`);
    });
}

async function reading(page: Page, name: string): Promise<void> {
    await page.evaluate(async () => { await document.fonts.ready; });
    const facts = await page.evaluate(() => {
        const list = document.querySelector('[aria-label="Issue list"]');
        const detail = document.querySelector("#issue-detail");
        function box(element: Element | null) {
            if (!element) return null;
            const bounds = element.getBoundingClientRect();
            return { x: bounds.x, y: bounds.y, width: bounds.width, bottom: bounds.bottom };
        }
        const unreadable = Array.from(document.querySelectorAll(".readable, h1, h2, button")).some(element => {
            const range = document.createRange();
            range.selectNodeContents(element);
            const text = range.getBoundingClientRect();
            return text.right > element.getBoundingClientRect().right + 1;
        });
        return {
            overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
            unreadable,
            list: box(list),
            detail: box(detail),
        };
    });
    assert.equal(facts.overflow, false, name);
    assert.equal(facts.unreadable, false, name);
    observations.push({ name, ...facts });
}

async function issuePage(path: string, width: number): Promise<void> {
    process.stdout.write(`${path} ${width}\n`);
    const page = await browser.newPage({ viewport: { width, height: 900 } });
    monitor(page);
    await page.goto(server.url + path);
    await page.getByRole("heading", { name: "Issue triage" }).waitFor();
    assert.equal(await page.locator("[data-choice]").count(), 3);
    await page.locator('[data-choice="2"]').click();
    await page.getByRole("button", { name: "Insert issue", exact: true }).click();
    assert.equal(await page.locator("[data-choice]").count(), 4);
    assert.match(await page.locator("#issue-detail").innerText(), /SELECTED ISSUE\s*2/);
    await reading(page, `${path}/${width}/insert-selection`);
    await compareDomain(page, path, width, "insert-selection");
    const fill = await page.evaluate(() => {
        const layout = document.querySelector('[data-machina-box="root"]') ?? document.querySelector(".baseline-issues");
        return layout ? getComputedStyle(layout).backgroundColor : undefined;
    });
    assert.equal(fill, "rgb(232, 238, 240)", "Declared layout style must reach the browser");
    if (width >= 600) {
        const bounds = await page.locator('[aria-label="Issue list"]').boundingBox();
        assert.equal(bounds?.width, 300);
    }
    await page.getByRole("button", { name: "Reverse issues", exact: true }).click();
    assert.equal(await page.locator("[data-choice]").first().getAttribute("data-choice"), "4");
    await page.getByRole("button", { name: "Change arrangement", exact: true }).click();
    await page.waitForFunction(() => {
        const list = document.querySelector('[aria-label="Issue list"]')?.getBoundingClientRect();
        const detail = document.querySelector("#issue-detail")?.getBoundingClientRect();
        return list && detail && detail.top >= list.bottom - 1;
    }).catch(async error => {
        process.stderr.write(JSON.stringify({ path, width, errors, html: await page.locator("#app").innerHTML() }, null, 2) + "\n");
        throw error;
    });
    assert.equal(await page.getByRole("button", { name: "Change arrangement", exact: true }).getAttribute("aria-pressed"), "true");
    await page.getByRole("button", { name: "Change arrangement", exact: true }).click();
    await page.setViewportSize({ width: 390, height: 900 });
    await page.waitForFunction(() => {
        const list = document.querySelector('[aria-label="Issue list"]')?.getBoundingClientRect();
        const detail = document.querySelector("#issue-detail")?.getBoundingClientRect();
        return list && detail && detail.top >= list.bottom - 1;
    });
    await reading(page, `${path}/${width}/live-resize`);
    await page.setViewportSize({ width, height: 900 });
    await page.getByRole("button", { name: "Change copy length", exact: true }).click();
    await page.getByText("This final sentence must remain reachable.", { exact: false }).waitFor();
    await reading(page, `${path}/${width}/long-copy`);
    await compareDomain(page, path, width, "long-copy");
    await page.evaluate(() => { document.documentElement.style.fontSize = "32px"; });
    await reading(page, `${path}/${width}/text-200-percent`);
    await page.evaluate(() => { document.documentElement.style.fontSize = ""; });
    await page.getByRole("button", { name: "Show loading", exact: true }).click();
    await page.getByRole("status").filter({ hasText: "Loading" }).waitFor();
    await page.getByRole("button", { name: "Show error", exact: true }).click();
    await page.getByRole("alert").waitFor();
    await page.getByRole("button", { name: "Retry", exact: true }).click();
    await page.locator("[data-choice]").first().waitFor();
    await page.locator("#search").fill("no such title");
    await page.getByRole("button", { name: "Search", exact: true }).click();
    await page.getByText("No matching issues.", { exact: true }).waitFor();
    await page.locator("#search").fill("");
    await page.getByRole("button", { name: "Search", exact: true }).click();
    await page.locator('[data-choice="2"]').click();
    await page.getByRole("button", { name: "Remove selected issue", exact: true }).click();
    assert.equal(await page.locator("[data-choice]").count(), 3);
    await page.getByText("Select an issue to read its details.", { exact: true }).waitFor();
    await page.screenshot({ path: `${evidence}/${path.includes("baseline") ? "baseline" : path.split("/")[1]}-${width}.png`, fullPage: true });
    await page.close();
}

async function lifecycle(profile: string): Promise<void> {
    const page = await browser.newPage();
    monitor(page);
    await page.goto(`${server.url}/${profile}/lifecycle/`);
    await page.locator("#amber").waitFor();
    assert.equal(await page.evaluate(() => {
        const log = Reflect.get(globalThis, Symbol.for("copeland.lifecycle-demo.host")) as Observation[];
        return log.length;
    }), 0);
    await page.locator("#amber").fill("Preserved draft");
    await page.getByRole("button", { name: "Reverse", exact: true }).click();
    assert.equal(await page.locator("#amber").inputValue(), "Preserved draft");
    observations.push({ name: `${profile}/keyed-reorder` });
    await page.getByRole("button", { name: "Focus amber", exact: true }).click();
    await page.waitForFunction(() => document.activeElement?.id === "amber");
    observations.push({ name: `${profile}/presentation-focus` });
    await page.locator('[aria-label="amber"]').getByRole("button", { name: "Save", exact: true }).click();
    await page.getByText("Saved: Preserved draft", { exact: true }).waitFor();
    observations.push({ name: `${profile}/typed-completion-real-http` });
    let releaseSlow: (() => void) | undefined;
    const slow = new Promise<void>(done => { releaseSlow = done; });
    await page.route("**/lifecycle-api/search?*", async route => {
        const query = new URL(route.request().url()).searchParams.get("query");
        if (query === "slow") await slow;
        await route.fulfill({ json: { ok: true, value: query === "none" ? "none" : "all" } });
    });
    await page.locator("#directory-query").fill("slow");
    await page.waitForFunction(() => {
        const log = Reflect.get(globalThis, Symbol.for("copeland.lifecycle-demo.host")) as Observation[];
        return log.some(item => item.kind === "search" && item.value === "slow");
    });
    await page.locator("#directory-query").fill("none");
    await page.locator("#amber").waitFor({ state: "detached" });
    releaseSlow!();
    await page.waitForFunction(() => {
        const registry = Reflect.get(globalThis, Symbol.for("copeland.ts.vue-projections.v1")) as Registry;
        return [...registry.instances.values()].some(frame => frame.trace.filter(entry => entry.kind === "EffectCompleted").length >= 2);
    });
    assert.equal(await page.locator("#amber").count(), 0);
    observations.push({ name: `${profile}/stale-search` });
    await page.locator("#directory-query").fill("all");
    await page.locator("#blue").waitFor();
    await page.locator("#blue").fill("Retired draft");
    await page.getByRole("button", { name: "Remove blue", exact: true }).click();
    await page.locator("#blue").waitFor({ state: "detached" });
    await page.getByRole("button", { name: "Restore blue", exact: true }).click();
    await page.locator("#blue").waitFor();
    assert.equal(await page.locator("#blue").inputValue(), "Blue draft");
    observations.push({ name: `${profile}/removed-key-fresh-owner` });
    await page.route("**/lifecycle-api/save?*", async route => {
        await route.fulfill({ json: { ok: false, error: "service rejected draft" } });
    });
    await page.locator('[aria-label="blue"]').getByRole("button", { name: "Save", exact: true }).click();
    await page.getByText("Failed: service rejected draft", { exact: true }).waitFor();
    observations.push({ name: `${profile}/typed-error` });
    let releaseSave: (() => void) | undefined;
    const delayedSave = new Promise<void>(done => { releaseSave = done; });
    await page.route("**/lifecycle-api/save?*", async route => {
        await delayedSave;
        await route.fulfill({ json: { ok: true, value: "Retired completion" } });
    });
    await page.evaluate(() => {
        const registry = Reflect.get(globalThis, Symbol.for("copeland.ts.vue-projections.v1")) as Registry;
        const old = [...registry.instances.values()].find(frame => frame.getSnapshot().presentation.props?.["aria-label"] === "blue");
        Reflect.set(globalThis, "__retiredBlue", old);
    });
    await page.locator('[aria-label="blue"]').getByRole("button", { name: "Save", exact: true }).click();
    await page.locator('[aria-label="blue"]').getByText("Saving", { exact: true }).waitFor();
    await page.getByRole("button", { name: "Remove blue", exact: true }).click();
    await page.locator("#blue").waitFor({ state: "detached" });
    await page.getByRole("button", { name: "Restore blue", exact: true }).click();
    await page.locator("#blue").waitFor();
    await page.locator("#blue").fill("Fresh owner draft");
    const oldResponse = page.waitForResponse(response => response.url().includes("/lifecycle-api/save"));
    releaseSave!();
    await oldResponse;
    await page.evaluate(() => {
        const old = Reflect.get(globalThis, "__retiredBlue") as Frame;
        old.dispatch("Edit", ["Stale endpoint write"]);
    });
    assert.equal(await page.locator("#blue").inputValue(), "Fresh owner draft");
    assert.equal(await page.getByText("Retired completion", { exact: false }).count(), 0);
    observations.push({ name: `${profile}/retired-owner-completion-and-endpoint` });
    await page.evaluate(() => {
        const registry = Reflect.get(globalThis, Symbol.for("copeland.ts.vue-projections.v1")) as Registry;
        if (registry.diagnostics.length !== 1 || registry.diagnostics[0].id !== "COPE-COMPONENT-STATE-0103") {
            throw new Error(JSON.stringify(registry.diagnostics));
        }
        const demo = Reflect.get(globalThis, "__vueDemo") as { app: { unmount(): void } };
        demo.app.unmount();
    });
    await page.waitForFunction(() => {
        const registry = Reflect.get(globalThis, Symbol.for("copeland.ts.vue-projections.v1")) as Registry;
        return registry.instances.size === 0;
    });
    observations.push({ name: `${profile}/root-retires-subtree` });
    await page.close();
}

try {
    for (const implementation of ["baseline", "diagnostic/application", "symbolic/application", "production/application"]) {
        for (const width of [1280, 768, 390]) await issuePage(`/${implementation}/`, width);
    }
    for (const profile of ["diagnostic", "symbolic", "production"]) await lifecycle(profile);
    assert.deepEqual(errors, []);
    const compiler = JSON.parse(await readFile(`${evidence}/compiler-report.json`, "utf8"));
    await writeFile(`${evidence}/browser-report.json`, JSON.stringify({
        milestone: "CTS-VUE-FRICTION-M8",
        browser: browser.version(),
        compiler,
        errors,
        observations,
    }, null, 2) + "\n");
    process.stdout.write(`Passed ${observations.length} observations with no browser diagnostics.\n`);
} finally {
    await browser.close();
    await server.close();
}
