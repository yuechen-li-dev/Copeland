import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { chromium, type Page } from "playwright";
import { startServer } from "./server.ts";

const evidence = fileURLToPath(new URL("../../../../docs/Copeland/reviews/evidence/tablescript-editing-m14/", import.meta.url));
const screenshots = fileURLToPath(new URL("../artifacts/screenshots/", import.meta.url));
await mkdir(evidence, { recursive: true });
await mkdir(screenshots, { recursive: true });
const server = await startServer();
const errors: string[] = [];
const observations: Record<string, unknown>[] = [];
const browser = await chromium.launch({ channel: process.env.FRICTION_BROWSER_CHANNEL ?? "msedge" });

async function snapshot(page: Page) {
    return page.evaluate(() => ({
        cents: document.getElementById("total-cents")?.textContent,
        units: document.getElementById("total-units")?.textContent,
        lines: document.getElementById("total-lines")?.textContent,
        selected: document.getElementById("selected-product")?.textContent,
        stock: (document.getElementById("on-hand") as HTMLInputElement)?.value,
        price: (document.getElementById("unit-price") as HTMLInputElement)?.value,
        rows: Array.from(document.querySelectorAll("[data-product]")).map(row => ({
            id: row.getAttribute("data-product"),
            onHand: row.querySelector('[data-field="onHand"]')?.textContent,
            price: row.querySelector('[data-field="price"]')?.textContent,
            orderUnits: row.querySelector('[data-field="orderUnits"]')?.textContent,
            orderCents: row.querySelector('[data-field="orderCents"]')?.textContent,
        })),
        errors: Array.from(document.querySelectorAll('[role="alert"]')).map(element => ({ id: element.id, text: element.textContent })),
        horizontalOverflow: document.documentElement.scrollWidth > document.documentElement.clientWidth + 1,
        missingDescriptions: Array.from(document.querySelectorAll("[aria-describedby]")).flatMap(element =>
            (element.getAttribute("aria-describedby") ?? "").split(" ").filter(id => !document.getElementById(id))),
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
    assert.deepEqual(facts.missingDescriptions, []);
    return facts;
}

async function expectText(page: Page, label: string, value: string) {
    const field = page.getByLabel(label, { exact: true });
    await field.fill(value);
    assert.equal(await field.inputValue(), value);
    await field.blur();
    assert.equal(await field.inputValue(), value);
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
                if (response.status() >= 400) {
                    errors.push(`${profile}/${width}: HTTP ${response.status()} ${response.url()}`);
                }
            });
            await page.goto(`${server.url}/${profile}/index.html`);
            const baseline = await expectTotals(page, 31210, 67, 3);
            assert.equal(baseline.stock, "4");
            assert.equal(baseline.price, "9.25");
            const probe = await page.evaluate(() => {
                const proof = Reflect.get(globalThis, "cafeProof") as { editingProbe(): number[] };
                return proof.editingProbe();
            });
            assert.deepEqual(probe, [31210, 31210, 12710, 2, 310, 15750, 15750, 1, 500, 15750, 1, 1, 31210]);
            await page.screenshot({ path: `${screenshots}/${profile}-${width}-baseline.png`, fullPage: true });

            // Drafts survive blur. Invalid text never substitutes an old numeric value.
            for (const text of ["-", "", "1.5", "2147483648", "1e2", " 1"]) {
                await expectText(page, "On hand", text);
                await page.getByRole("button", { name: "Apply stock", exact: true }).click();
                await page.locator("#stock-parse-error").waitFor();
                assert.equal(await page.getByLabel("On hand", { exact: true }).getAttribute("aria-invalid"), "true");
                assert.deepEqual((await expectTotals(page, 31210, 67, 3)).rows, baseline.rows);
            }
            await page.getByRole("button", { name: "Cancel stock", exact: true }).click();
            assert.equal(await page.getByLabel("On hand", { exact: true }).inputValue(), "4");
            assert.equal(await page.getByRole("alert").count(), 0);

            await expectText(page, "On hand", "-1");
            await page.getByRole("button", { name: "Apply stock", exact: true }).click();
            await page.locator("#stock-domain-error").waitFor();
            assert.match(await page.locator("#stock-domain-error").innerText(), /cannot be negative/);
            assert.equal(await page.locator("#stock-parse-error").count(), 0);
            assert.deepEqual((await expectTotals(page, 31210, 67, 3)).rows, baseline.rows);

            // Applying one field preserves the other field's unsaved draft.
            await expectText(page, "Unit price", "1.");
            await expectText(page, "On hand", "12");
            await page.getByLabel("On hand", { exact: true }).press("Enter");
            const stock = await expectTotals(page, 12710, 47, 2);
            assert.equal(stock.rows[0].onHand, "12");
            assert.equal(stock.price, "1.");
            assert.equal(stock.errors.length, 0);
            assert.equal(await page.locator("#stock-status").innerText(), "Matches accepted value");
            assert.equal(await page.locator("#price-status").innerText(), "Unapplied changes");

            // Selection discards drafts and loads the latest accepted keyed snapshot.
            await page.getByRole("button", { name: "Edit 102", exact: true }).click();
            assert.equal(await page.getByLabel("On hand", { exact: true }).inputValue(), "2");
            assert.equal(await page.getByLabel("Unit price", { exact: true }).inputValue(), "3.10");
            for (const text of ["1.", "1.234", "1e2", ".50", "21474836.48"]) {
                await expectText(page, "Unit price", text);
                await page.getByRole("button", { name: "Apply price", exact: true }).click();
                await page.locator("#price-parse-error").waitFor();
                assert.deepEqual((await expectTotals(page, 12710, 47, 2)).rows, stock.rows);
            }
            const incomplete = await snapshot(page);
            await page.screenshot({ path: `${screenshots}/${profile}-${width}-parse-error.png`, fullPage: true });
            for (const text of ["-0.01", "10000.01"]) {
                await expectText(page, "Unit price", text);
                await page.getByRole("button", { name: "Apply price", exact: true }).click();
                await page.locator("#price-domain-error").waitFor();
                assert.equal(await page.locator("#price-parse-error").count(), 0);
                assert.deepEqual((await expectTotals(page, 12710, 47, 2)).rows, stock.rows);
            }
            await expectText(page, "On hand", "-");
            await expectText(page, "Unit price", "5.00");
            await page.getByRole("button", { name: "Apply price", exact: true }).click();
            const price = await expectTotals(page, 15750, 47, 2);
            assert.equal(price.stock, "-");
            assert.equal(price.rows[2].price, "5.00");
            assert.equal(price.rows[2].orderCents, "8000");
            const acceptedPrices = [];
            for (const boundary of [
                { text: "0", cents: 7750, normalized: "0.00" },
                { text: "10000.00", cents: 16007750, normalized: "10000.00" },
                { text: "+0005.0", cents: 15750, normalized: "5.00" },
            ]) {
                await expectText(page, "Unit price", boundary.text);
                await page.getByRole("button", { name: "Apply price", exact: true }).click();
                const accepted = await expectTotals(page, boundary.cents, 47, 2);
                assert.equal(accepted.price, boundary.normalized);
                assert.equal(accepted.stock, "-");
                assert.equal(accepted.errors.length, 0);
                acceptedPrices.push({ text: boundary.text, normalized: accepted.price, totalCents: accepted.cents });
            }
            await page.getByRole("button", { name: "Cancel stock", exact: true }).click();
            assert.equal(await page.getByLabel("On hand", { exact: true }).inputValue(), "2");
            await expectText(page, "Unit price", "6");
            await page.getByRole("button", { name: "Cancel price", exact: true }).click();
            assert.equal(await page.getByLabel("Unit price", { exact: true }).inputValue(), "5.00");
            await page.getByRole("button", { name: "Edit 100", exact: true }).click();
            assert.equal(await page.getByLabel("On hand", { exact: true }).inputValue(), "12");
            await page.getByRole("button", { name: "Edit 102", exact: true }).click();
            assert.equal(await page.getByLabel("Unit price", { exact: true }).inputValue(), "5.00");
            await page.screenshot({ path: `${screenshots}/${profile}-${width}-revised.png`, fullPage: true });

            await expectText(page, "On hand", "-");
            await expectText(page, "Unit price", "1.");
            await page.getByRole("button", { name: "Reset preview", exact: true }).click();
            const reset = await expectTotals(page, 31210, 67, 3);
            assert.deepEqual(reset.rows, baseline.rows);
            assert.equal(reset.stock, "4");
            assert.equal(reset.price, "9.25");
            assert.equal(reset.errors.length, 0);
            // Also reset incomplete text when accepted numeric values never changed.
            await expectText(page, "On hand", "-");
            await expectText(page, "Unit price", "1.");
            await page.getByRole("button", { name: "Reset preview", exact: true }).click();
            assert.equal(await page.getByLabel("On hand", { exact: true }).inputValue(), "4");
            assert.equal(await page.getByLabel("Unit price", { exact: true }).inputValue(), "9.25");
            if (width === 390) {
                const scroll = page.getByLabel("Scrollable stock table", { exact: true });
                assert.equal(await scroll.evaluate(element => element.scrollWidth > element.clientWidth), true);
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
            observations.push({ profile, width, probe, baseline, stock, incomplete, price, acceptedPrices, reset, globalCents });
            await page.close();
            process.stdout.write(`${profile}/${width}: typed drafts, parse/domain errors, Apply/Cancel/Select/Reset verified\n`);
        }
    }
    assert.deepEqual(errors, []);
    await writeFile(`${evidence}/browser-report.json`, JSON.stringify({
        milestone: "CTS-TABLESCRIPT-EDITING-M14", nodeVersion: process.version,
        browserVersion: browser.version(), errors, observations,
    }, null, 2) + "\n");
} finally {
    await browser.close();
    await server.close();
}
