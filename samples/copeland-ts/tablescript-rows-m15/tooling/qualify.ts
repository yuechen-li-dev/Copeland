import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { chromium, type Page } from "playwright";
import { startServer } from "./server.ts";

const evidence = fileURLToPath(new URL("../../../../docs/Copeland/reviews/evidence/tablescript-rows-m15/", import.meta.url));
const screenshots = fileURLToPath(new URL("../artifacts/screenshots/", import.meta.url));
await mkdir(evidence, { recursive: true });
await mkdir(screenshots, { recursive: true });
const server = await startServer();
const browser = await chromium.launch({ channel: process.env.FRICTION_BROWSER_CHANNEL ?? "msedge" });
const errors: string[] = [];
const observations: Record<string, unknown>[] = [];

async function snapshot(page: Page) {
    return page.evaluate(() => ({
        cents: document.getElementById("total-cents")?.textContent,
        units: document.getElementById("total-units")?.textContent,
        lines: document.getElementById("total-lines")?.textContent,
        count: document.getElementById("row-count")?.textContent,
        selected: document.getElementById("selected-product")?.textContent ?? null,
        rows: Array.from(document.querySelectorAll("[data-product]")).map(row => ({
            id: row.getAttribute("data-product"),
            stock: row.querySelector('[data-field="onHand"]')?.textContent,
            price: row.querySelector('[data-field="price"]')?.textContent,
            cents: row.querySelector('[data-field="orderCents"]')?.textContent,
        })),
        errors: Array.from(document.querySelectorAll('[role="alert"]')).map(element => element.textContent),
        overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
        missingDescriptions: Array.from(document.querySelectorAll("[aria-describedby]")).flatMap(element =>
            (element.getAttribute("aria-describedby") ?? "").split(" ").filter(id => !document.getElementById(id))),
    }));
}

async function expectPlan(page: Page, count: number, cents: number) {
    await page.waitForFunction(expected => document.getElementById("total-cents")?.textContent === expected, String(cents));
    const facts = await snapshot(page);
    assert.equal(facts.count, String(count));
    assert.equal(facts.rows.length, count);
    assert.equal(facts.cents, String(cents));
    assert.equal(facts.overflow, false);
    assert.deepEqual(facts.missingDescriptions, []);
    return facts;
}

async function request(page: Page, id: string, supplier = "30", stock = "2", price = "5.00") {
    await page.getByLabel("New product ID", { exact: true }).fill(id);
    await page.getByLabel("Product name", { exact: true }).fill("House chocolate");
    await page.getByLabel("Supplier ID", { exact: true }).fill(supplier);
    await page.getByLabel("Initial stock", { exact: true }).fill(stock);
    await page.getByLabel("New unit price", { exact: true }).fill(price);
    await page.getByRole("button", { name: "Create product", exact: true }).click();
}

try {
    for (const profile of ["diagnostic", "symbolic", "production"]) {
        for (const width of [1280, 390]) {
            const page = await browser.newPage({ viewport: { width, height: 960 } });
            page.on("pageerror", error => errors.push(`${profile}/${width}: ${error.stack ?? error.message}`));
            page.on("console", message => {
                if (message.type() === "error" || message.type() === "warning") {
                    errors.push(`${profile}/${width}: ${message.text()}`);
                }
            });
            page.on("response", response => {
                if (response.status() >= 400) errors.push(`${profile}/${width}: HTTP ${response.status()} ${response.url()}`);
            });
            await page.goto(`${server.url}/${profile}/index.html`);
            const baseline = await expectPlan(page, 6, 31210);
            const probe = await page.evaluate(() => {
                const proof = Reflect.get(globalThis, "cafeProof") as { rowsProbe(): number[] };
                return proof.rowsProbe();
            });
            assert.deepEqual(probe, [6, 7, 42210, 1, 1, 1, 6, 31210, 0, 0, 0, 1, 11000, 31210]);
            const limits = await page.evaluate(() => {
                const proof = Reflect.get(globalThis, "cafeProof") as {
                    totalLimitProbe(): number[];
                    editLimitProbe(): number[];
                };
                return { creation: proof.totalLimitProbe(), editing: proof.editLimitProbe() };
            });
            assert.deepEqual(limits.creation, [95, 2136031210, 1]);
            assert.deepEqual(limits.editing, [2136031210, 1, 925]);

            await request(page, "106", "30", "2", "1.");
            await page.locator("#new-price-parse-error").waitFor();
            assert.equal(await page.getByLabel("New unit price", { exact: true }).inputValue(), "1.");
            assert.deepEqual((await expectPlan(page, 6, 31210)).rows, baseline.rows);
            await request(page, "106", "999");
            await page.locator("#row-error").waitFor();
            assert.match(await page.locator("#row-error").innerText(), /references missing key 999/);
            assert.deepEqual((await expectPlan(page, 6, 31210)).rows, baseline.rows);
            await request(page, "106", "30", "-1");
            assert.match(await page.locator("#row-error").innerText(), /cannot be negative/);
            assert.deepEqual((await expectPlan(page, 6, 31210)).rows, baseline.rows);
            await request(page, "106");
            const added = await expectPlan(page, 7, 42210);
            assert.equal(added.rows[6].id, "106");
            assert.equal(added.units, "89");
            assert.equal(added.lines, "4");
            assert.match(added.selected ?? "", /106/);
            await page.screenshot({ path: `${screenshots}/${profile}-${width}-added.png`, fullPage: true });

            await request(page, "106");
            await page.locator("#row-error").waitFor();
            assert.match(await page.locator("#row-error").innerText(), /already contains key 106/);
            const duplicate = await expectPlan(page, 7, 42210);
            assert.deepEqual(duplicate.rows, added.rows);
            await page.getByRole("button", { name: "Cancel creation", exact: true }).click();
            assert.equal(await page.locator("#row-error").count(), 0);

            // Ordinary editing follows the appended row by key, beyond seed bounds.
            await page.getByLabel("On hand", { exact: true }).fill("3");
            await page.getByRole("button", { name: "Apply stock", exact: true }).click();
            await expectPlan(page, 7, 41710);
            await page.getByLabel("Unit price", { exact: true }).fill("6.00");
            await page.getByRole("button", { name: "Apply price", exact: true }).click();
            const edited = await expectPlan(page, 7, 43810);
            assert.equal(edited.rows[6].stock, "3");
            assert.equal(edited.rows[6].price, "6.00");
            await page.getByLabel("On hand", { exact: true }).fill("-");
            await page.getByRole("button", { name: "Remove 106", exact: true }).click();
            const removed = await expectPlan(page, 6, 31210);
            assert.equal(removed.selected, null);
            await page.locator("#no-selection").waitFor();
            assert.deepEqual(removed.rows, baseline.rows);

            // Removing another row keeps the selected identity and incomplete draft.
            await page.getByRole("button", { name: "Edit 102", exact: true }).click();
            await page.getByLabel("On hand", { exact: true }).fill("-");
            await page.getByRole("button", { name: "Remove 101", exact: true }).click();
            const unrelated = await expectPlan(page, 5, 31210);
            assert.match(unrelated.selected ?? "", /102/);
            assert.equal(await page.getByLabel("On hand", { exact: true }).inputValue(), "-");
            for (const id of [100, 102, 103, 104, 105]) {
                await page.getByRole("button", { name: `Remove ${id}`, exact: true }).click();
            }
            const empty = await expectPlan(page, 0, 0);
            assert.equal(empty.units, "0");
            assert.equal(empty.lines, "0");
            assert.equal(empty.selected, null);
            await page.locator("#empty-state").waitFor();
            assert.equal(await page.locator("[data-supplier]").count(), 0);
            await page.screenshot({ path: `${screenshots}/${profile}-${width}-empty.png`, fullPage: true });
            await request(page, "107");
            const restored = await expectPlan(page, 1, 11000);
            assert.equal(restored.rows[0].id, "107");
            assert.match(restored.selected ?? "", /107/);
            await page.screenshot({ path: `${screenshots}/${profile}-${width}-restored.png`, fullPage: true });
            await page.getByLabel("New product ID", { exact: true }).fill("-");
            await page.getByRole("button", { name: "Reset preview", exact: true }).click();
            const reset = await expectPlan(page, 6, 31210);
            assert.deepEqual(reset.rows, baseline.rows);
            assert.equal(await page.getByLabel("New product ID", { exact: true }).inputValue(), "106");
            if (width === 390) {
                const scroll = page.getByLabel("Scrollable stock table", { exact: true });
                await scroll.evaluate(element => { element.scrollLeft = 0; });
                await scroll.focus();
                await page.keyboard.press("ArrowRight");
                await page.waitForFunction(() => (document.querySelector(".table-scroll")?.scrollLeft ?? 0) > 0);
            }
            const globalCents = await page.evaluate(() => {
                const proof = Reflect.get(globalThis, "cafeProof") as { baselineCents(): number };
                return proof.baselineCents();
            });
            assert.equal(globalCents, 31210);
            observations.push({ profile, width, probe, limits, baseline, added, duplicate, edited, removed, unrelated, empty, restored, reset, globalCents });
            await page.close();
            process.stdout.write(`${profile}/${width}: create, refusal, keyed edit/delete, empty, restore, reset verified\n`);
        }
    }
    assert.deepEqual(errors, []);
    await writeFile(`${evidence}/browser-report.json`, JSON.stringify({
        milestone: "CTS-TABLESCRIPT-ROWS-M15", nodeVersion: process.version,
        browserVersion: browser.version(), errors, observations,
    }, null, 2) + "\n");
} finally {
    await browser.close();
    await server.close();
}
