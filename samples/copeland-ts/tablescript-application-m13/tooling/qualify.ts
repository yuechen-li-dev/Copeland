import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { chromium, type Page } from "playwright";
import { startServer } from "./server.ts";

const evidence = fileURLToPath(new URL("../../../../docs/Copeland/reviews/evidence/tablescript-application-m13/", import.meta.url));
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
        rows: Array.from(document.querySelectorAll("[data-product]")).map(row => ({
            id: row.getAttribute("data-product"),
            onHand: row.querySelector('[data-field="onHand"]')?.textContent,
            orderUnits: row.querySelector('[data-field="orderUnits"]')?.textContent,
            orderCents: row.querySelector('[data-field="orderCents"]')?.textContent,
        })),
        suppliers: Array.from(document.querySelectorAll("[data-supplier]")).map(row => row.getAttribute("data-supplier")),
        error: document.querySelector('[role="alert"]')?.textContent ?? null,
        horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
    }));
}

async function expectTotals(page: Page, cents: number, units: number, lines: number) {
    await page.waitForFunction(expected => document.getElementById("total-cents")?.textContent === expected, String(cents));
    const facts = await snapshot(page);
    assert.equal(facts.cents, String(cents));
    assert.equal(facts.units, String(units));
    assert.equal(facts.lines, String(lines));
    assert.equal(facts.rows.length, 6);
    assert.equal(facts.horizontalOverflow, false);
    return facts;
}

try {
    for (const profile of ["diagnostic", "symbolic", "production"]) {
        for (const width of [1280, 390]) {
            const page = await browser.newPage({ viewport: { width, height: 960 } });
            page.on("pageerror", error => errors.push(`${profile}/${width}: ${error.stack ?? error.message}`));
            page.on("console", message => {
                if (message.type() === "error" || message.type() === "warning") errors.push(`${profile}/${width}: ${message.text()}`);
            });
            page.on("response", response => {
                if (response.status() >= 400) errors.push(`${profile}/${width}: HTTP ${response.status()} ${response.url()}`);
            });
            await page.goto(`${server.url}/${profile}/index.html`);
            const baseline = await expectTotals(page, 31210, 67, 3);
            assert.equal(baseline.rows[0].onHand, "4");
            assert.equal(baseline.rows[3].onHand, "5");
            const probe = await page.evaluate(() => {
                const proof = Reflect.get(globalThis, "cafeProof") as { applicationProbe(): number[] };
                return proof.applicationProbe();
            });
            assert.deepEqual(probe, [31210, 12710, 47, 2, 6, 12, 5, 2, 20, 30, 1, 1, 31210]);
            await page.screenshot({ path: `${screenshots}/${profile}-${width}-baseline.png`, fullPage: true });
            if (width === 390) {
                const scroll = page.getByLabel("Scrollable stock table", { exact: true });
                assert.equal(await scroll.evaluate(element => element.scrollWidth > element.clientWidth), true);
                await scroll.focus();
                await page.keyboard.press("ArrowRight");
                await page.waitForFunction(() => (document.querySelector(".table-scroll")?.scrollLeft ?? 0) > 0);
                await scroll.evaluate(element => { element.scrollLeft = 0; });
            }
            await page.getByLabel("On hand", { exact: true }).fill("12");
            await page.getByRole("button", { name: "Apply preview", exact: true }).click();
            const revised = await expectTotals(page, 12710, 47, 2);
            assert.equal(revised.rows[0].onHand, "12");
            assert.deepEqual(revised.suppliers, ["20", "30"]);
            await page.screenshot({ path: `${screenshots}/${profile}-${width}-revised.png`, fullPage: true });
            await page.getByLabel("On hand", { exact: true }).fill("-1");
            await page.getByRole("button", { name: "Apply preview", exact: true }).click();
            await page.getByRole("alert").waitFor();
            const negative = await expectTotals(page, 12710, 47, 2);
            assert.match(negative.error ?? "", /product 100 cannot be negative.*-1/);
            assert.deepEqual(negative.rows, revised.rows);
            await page.screenshot({ path: `${screenshots}/${profile}-${width}-negative.png`, fullPage: true });
            await page.getByLabel("Product ID", { exact: true }).fill("999");
            await page.getByLabel("On hand", { exact: true }).fill("2");
            await page.getByRole("button", { name: "Apply preview", exact: true }).click();
            await page.waitForFunction(() => document.querySelector('[role="alert"]')?.textContent?.includes("999"));
            const missing = await expectTotals(page, 12710, 47, 2);
            assert.match(missing.error ?? "", /Product 999 does not exist/);
            assert.deepEqual(missing.rows, revised.rows);
            await page.getByRole("button", { name: "Edit 103", exact: true }).click();
            await page.getByLabel("On hand", { exact: true }).fill("20");
            await page.getByRole("button", { name: "Apply preview", exact: true }).click();
            const chained = await expectTotals(page, 4960, 16, 1);
            assert.equal(chained.rows[0].onHand, "12");
            assert.equal(chained.rows[3].onHand, "20");
            assert.equal(chained.error, null);
            // Invalid DOM text must not submit the last valid application draft.
            await page.getByLabel("On hand", { exact: true }).fill("1.5");
            await page.getByRole("button", { name: "Apply preview", exact: true }).click();
            assert.equal(await page.getByLabel("On hand", { exact: true }).evaluate(element => (element as HTMLInputElement).validity.valid), false);
            assert.deepEqual((await snapshot(page)).rows, chained.rows);
            await page.getByRole("button", { name: "Reset preview", exact: true }).click();
            const reset = await expectTotals(page, 31210, 67, 3);
            assert.deepEqual(reset.rows, baseline.rows);
            assert.equal(await page.getByLabel("On hand", { exact: true }).evaluate(element => (element as HTMLInputElement).validity.valid), true);
            // Reset must also discard invalid text when the semantic int was already 4.
            await page.getByLabel("On hand", { exact: true }).fill("1.5");
            await page.getByRole("button", { name: "Reset preview", exact: true }).click();
            await page.waitForFunction(() => {
                const input = document.getElementById("on-hand") as HTMLInputElement;
                return input.value === "4" && input.validity.valid;
            });
            const globalCents = await page.evaluate(() => {
                const proof = Reflect.get(globalThis, "cafeProof") as { baselineCents(): number };
                return proof.baselineCents();
            });
            assert.equal(globalCents, 31210);
            observations.push({ profile, width, probe, baseline, revised, negative, missing, chained, reset, globalCents });
            await page.close();
            process.stdout.write(`${profile}/${width}: edit, refusal, chained snapshot, reset verified\n`);
        }
    }
    assert.deepEqual(errors, []);
    await writeFile(`${evidence}/browser-report.json`, JSON.stringify({ milestone: "CTS-TABLESCRIPT-APPLICATION-M13", nodeVersion: process.version, browserVersion: browser.version(), errors, observations }, null, 2) + "\n");
} finally {
    await browser.close();
    await server.close();
}
