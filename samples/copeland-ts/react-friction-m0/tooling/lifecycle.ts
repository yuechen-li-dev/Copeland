import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { dirname } from "node:path";
import { startServer } from "./server.ts";

type Route = import("../baseline/node_modules/playwright/index.js").Route;
interface ElementView {
    props: { "aria-label"?: string };
}
interface Frame {
    identity: string;
    endpoints: Record<string, unknown>;
    trace: { kind: string; revision: number; detail?: string }[];
    getSnapshot(): { presentation: ElementView; revision: number };
}
interface Registry {
    instances: Map<string, Frame>;
    diagnostics: { id: string; message: string }[];
}
interface Demo {
    Directory(): unknown;
    Broken(): unknown;
    Duplicate(): unknown;
    Phases(): unknown;
    SendText(endpoint: unknown, value: string): void;
    SendNumber(endpoint: unknown, value: number): void;
}
interface ProbeState {
    demo: Demo;
    directory: Frame;
    amber: Frame;
    blue: Frame;
}
interface HostObservation {
    kind: string;
    value: string;
    id?: string;
}
type ProbeGlobal = typeof globalThis & { __lifecycleProbe: ProbeState };
const require = createRequire(new URL("../baseline/package.json", import.meta.url));
const { chromium } = require("playwright") as typeof import("../baseline/node_modules/playwright/index.js");
const { build } = require("esbuild") as typeof import("../baseline/node_modules/esbuild/lib/main.js");
const fixture = fileURLToPath(new URL("../", import.meta.url));
const evidenceName = process.env.FRICTION_EVIDENCE_NAME ?? "react-effects-children-m7";
const evidence = fileURLToPath(new URL(`../../../../docs/Copeland/reviews/evidence/${evidenceName}/`, import.meta.url));
const compiler = JSON.parse(await readFile(`${evidence}/compiler-report.json`, "utf8"));
const profile = process.env.FRICTION_COMPONENT_PROFILE ?? "Diagnostic";
const specimen = compiler.results.find((result: { name: string }) => result.name === `lifecycle-${profile.toLowerCase()}`);
assert.equal(specimen?.expectationMatched, true);
const artifact = `${fixture}/artifacts/${evidenceName}/lifecycle-${profile.toLowerCase()}`;
await mkdir(artifact, { recursive: true });
const assets: Record<string, string> = {};
const emittedHashes: Record<string, string> = {};
for (const [name, source] of Object.entries(specimen.javascript as Record<string, string>)) {
    const path = `${artifact}/${name}`;
    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, source);
    assets[`/lifecycle/${name}`] = path;
    emittedHashes[name] = createHash("sha256").update(source).digest("hex");
}
for (const name of ["host", "entry"]) {
    const result = await build({ entryPoints: [`${fixture}/tooling/lifecycle-${name}.ts`], format: "esm", write: false });
    const path = `${artifact}/${name}.js`;
    await writeFile(path, result.outputFiles[0].text);
    assets[`/lifecycle/${name}.js`] = path;
}
let html = await readFile(`${fixture}/dist/browser/index.html`, "utf8");
html = html.replace(/<script type="importmap">([\s\S]*?)<\/script>/, (_, raw: string) => {
    const map = JSON.parse(raw);
    map.imports["@copeland/lifecycle-host"] = "/lifecycle/host.js";
    return `<script type="importmap">${JSON.stringify(map)}</script>`;
});
html = html.replace(/<script type="module" src="\.\/main\.js"><\/script>/, '<script type="module" src="/lifecycle/entry.js"></script>');
html = html.replace("<head>", '<head><base href="/">');
await writeFile(`${artifact}/index.html`, html);
assets["/lifecycle/"] = `${artifact}/index.html`;
const server = await startServer(0, undefined, assets);
if (process.argv.includes("--serve")) {
    process.stdout.write(`Lifecycle demonstration: ${server.url}/lifecycle/\nCtrl+C closes the foreground host.\n`);
    await new Promise<void>(resolve => {
        process.once("SIGINT", resolve);
        process.once("SIGTERM", resolve);
    });
    await server.close();
    process.exit(0);
}
const pending = new Map<string, Route[]>();
const observations: string[] = [];
const errors: string[] = [];
const browser = await chromium.launch({ channel: process.env.FRICTION_BROWSER_CHANNEL ?? "msedge", headless: true });
try {
    const page = await browser.newPage();
    page.on("pageerror", error => { errors.push(error.stack ?? error.message); process.stderr.write(error.message + "\n"); });
    page.on("console", message => {
        if (message.type() === "error" || message.type() === "warning") errors.push(message.text());
    });
    await page.route("**/lifecycle-api/**", route => {
        const url = new URL(route.request().url());
        const key = url.pathname.endsWith("save") ? `save:${url.searchParams.get("draft")}` : `search:${url.searchParams.get("query")}`;
        const requests = pending.get(key) ?? [];
        requests.push(route);
        pending.set(key, requests);
    });
    async function reply(key: string, value: string, ok = true): Promise<void> {
        await assertPending(key);
        const route = pending.get(key)!.shift()!;
        await route.fulfill({ contentType: "application/json", body: JSON.stringify(ok ? { ok, value } : { ok, error: value }) });
    }
    async function assertPending(key: string, count = 1): Promise<void> {
        const deadline = Date.now() + 10000;
        while ((pending.get(key)?.length ?? 0) < count) {
            if (Date.now() > deadline) throw new Error(`Missing request ${key}: ${JSON.stringify(errors)}`);
            await new Promise(resolve => setTimeout(resolve, 10));
        }
    }
    await page.goto(`${server.url}/lifecycle/`);
    await page.getByRole("heading", { name: "Owned editable search results" }).waitFor();
    await page.evaluate(async () => {
        const load = (specifier: string): Promise<unknown> => import(specifier);
        const ui = await load("/lifecycle/Demo.js");
        const probes = await load("/lifecycle/Probe.js");
        const demo = Object.assign({}, ui, probes) as Demo;
        const registry = Reflect.get(globalThis, Symbol.for("copeland.ts.react-projections.v1")) as Registry;
        const frames = [...registry.instances.values()];
        const editor = (id: string) => frames.find(frame => frame.getSnapshot().presentation.props["aria-label"] === id)!;
        const directory = frames.find(frame => frame.identity.includes("Directory::"))!;
        if (!directory || !editor("amber") || !editor("blue")) throw new Error("Missing canonical owners");
        (globalThis as ProbeGlobal).__lifecycleProbe = { demo, directory, amber: editor("amber"), blue: editor("blue") };
    });
    assert.equal(await page.evaluate(() => (Reflect.get(globalThis, Symbol.for("copeland.lifecycle-demo.host")) as HostObservation[]).length), 0);
    observations.push("StrictMode mount/replay performs no search, save, or focus commands.");
    const amber = page.getByRole("region", { name: "amber", exact: true });
    const blue = page.getByRole("region", { name: "blue", exact: true });
    await amber.getByRole("textbox").fill("edited amber");
    await blue.getByRole("textbox").fill("edited blue");
    await page.getByRole("button", { name: "Reverse", exact: true }).click();
    assert.equal(await amber.getByRole("textbox").inputValue(), "edited amber");
    assert.equal(await blue.getByRole("textbox").inputValue(), "edited blue");
    assert.equal(await page.locator("#app section").first().getAttribute("aria-label"), "blue");
    assert.equal(await page.evaluate(() => {
        const state = (globalThis as ProbeGlobal).__lifecycleProbe;
        const registry = Reflect.get(globalThis, Symbol.for("copeland.ts.react-projections.v1")) as Registry;
        return registry.instances.get(state.blue.identity) === state.blue && registry.instances.get(state.amber.identity) === state.amber;
    }), true);
    observations.push("Reordering keyed editors preserves draft state, canonical frames, and endpoint identity.");
    await page.evaluate(() => {
        const state = (globalThis as ProbeGlobal).__lifecycleProbe;
        state.demo.SendText(state.directory.endpoints.Focus, "amber");
        state.demo.SendText(state.directory.endpoints.Focus, "blue");
    });
    await page.waitForFunction(() => document.activeElement?.id === "blue");
    assert.deepEqual(await page.evaluate(() => (Reflect.get(globalThis, Symbol.for("copeland.lifecycle-demo.host")) as HostObservation[])
        .filter(item => item.kind === "focus").map(item => item.value)), ["blue"]);
    observations.push("Batched focus requests cancel the superseded revision and focus only the committed control.");
    await page.evaluate(() => {
        const state = (globalThis as ProbeGlobal).__lifecycleProbe;
        state.demo.SendText(state.directory.endpoints.Search, "old");
        state.demo.SendText(state.directory.endpoints.Search, "new");
    });
    await assertPending("search:old");
    await reply("search:new", "one");
    await blue.waitFor({ state: "detached" });
    await reply("search:old", "all");
    await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
    assert.equal(await page.locator("#app section").count(), 1);
    observations.push("Every accepted search launches despite batching; explicit request matching rejects an older result.");
    await page.getByRole("button", { name: "Restore blue" }).click();
    await blue.waitFor();
    assert.equal(await blue.getByRole("textbox").inputValue(), "Blue draft");
    assert.equal(await amber.getByRole("textbox").inputValue(), "edited amber");
    observations.push("Reinserting a removed key creates fresh state while surviving siblings retain theirs.");
    await page.evaluate(() => {
        const state = (globalThis as ProbeGlobal).__lifecycleProbe;
        state.demo.SendText(state.amber.endpoints.Save, "amber");
        state.demo.SendText(state.amber.endpoints.Save, "amber");
    });
    await assertPending("save:edited amber", 2);
    assert.equal(await page.evaluate(() => (Reflect.get(globalThis, Symbol.for("copeland.lifecycle-demo.host")) as HostObservation[])
        .filter(item => item.kind === "save" && item.id === "amber").length), 2);
    await reply("save:edited amber", "edited amber");
    await reply("save:edited amber", "edited amber");
    await amber.getByText("Saved: edited amber", { exact: true }).waitFor();
    observations.push("Two accepted saves launch twice, and StrictMode replay never duplicates either command.");
    await amber.getByRole("button", { name: "Save", exact: true }).click();
    await reply("save:edited amber", "backend refused", false);
    await amber.getByText("Failed: backend refused", { exact: true }).waitFor();
    observations.push("A service failure returns through a typed result and explicit status matching.");
    await blue.getByRole("textbox").fill("retired draft");
    await blue.getByRole("button", { name: "Save", exact: true }).click();
    await assertPending("save:retired draft");
    const retired = await page.evaluate(() => {
        const registry = Reflect.get(globalThis, Symbol.for("copeland.ts.react-projections.v1")) as Registry;
        const blue = [...registry.instances.values()].find(frame => frame.getSnapshot().presentation.props["aria-label"] === "blue")!;
        (globalThis as ProbeGlobal).__lifecycleProbe.blue = blue;
        return blue.identity;
    });
    await page.getByRole("button", { name: "Remove blue" }).click();
    await blue.waitFor({ state: "detached" });
    await page.getByRole("button", { name: "Restore blue" }).click();
    await blue.waitFor();
    await reply("save:retired draft", "retired draft");
    await page.evaluate(() => {
        const state = (globalThis as ProbeGlobal).__lifecycleProbe;
        state.demo.SendText(state.blue.endpoints.Edit, "late retained endpoint");
    });
    assert.equal(await blue.getByRole("textbox").inputValue(), "Blue draft");
    assert.equal(await blue.getByText("Ready", { exact: true }).count(), 1);
    assert.equal(await page.evaluate(identity => {
        const registry = Reflect.get(globalThis, Symbol.for("copeland.ts.react-projections.v1")) as Registry;
        return registry.instances.has(identity);
    }, retired), false);
    observations.push("Removed owner cancellation and retained endpoint rejection prevent late work entering a replacement with the same key.");
    await page.screenshot({ path: `${evidence}/lifecycle-${profile.toLowerCase()}.png`, fullPage: true });
    await page.evaluate(async () => {
        const state = (globalThis as ProbeGlobal).__lifecycleProbe;
        const ReactDOM = await (async (specifier: string): Promise<unknown> => import(specifier))("react-dom/client") as { createRoot(element: Element): { render(node: unknown): void } };
        for (const [id, node] of [["broken", state.demo.Broken()], ["duplicate", state.demo.Duplicate()], ["phases", state.demo.Phases()]] as [string, unknown][]) {
            const host = document.createElement("div");
            host.id = id;
            document.body.appendChild(host);
            ReactDOM.createRoot(host).render(node);
        }
    });
    await page.locator("#broken span").waitFor();
    await page.getByRole("button", { name: "Run phases" }).click();
    await page.waitForFunction(() => document.querySelector("#phase")?.textContent === "2");
    const phaseTrace = await page.evaluate(() => {
        const registry = Reflect.get(globalThis, Symbol.for("copeland.ts.react-projections.v1")) as Registry;
        return [...registry.instances.values()].find(frame => frame.getSnapshot().presentation.props["aria-label"] === "Phases")!.trace;
    });
    const phaseStart = phaseTrace.findIndex(item => item.kind === "EffectStarted" && item.detail === "PresentationCommitted");
    const completionCommit = phaseTrace.findIndex(item => item.kind === "StateCommitted" && item.revision === 2);
    assert.ok(phaseStart >= 0 && phaseStart < completionCommit);
    observations.push("A synchronous completion waits until the transition's presentation effect launches; completion reduction never nests inside a phase.");
    await page.getByRole("button", { name: "Duplicate", exact: true }).click();
    await page.evaluate(() => {
        const registry = Reflect.get(globalThis, Symbol.for("copeland.ts.react-projections.v1")) as Registry;
        const frame = [...registry.instances.values()].find(frame => frame.identity.includes("Broken::"))!;
        (globalThis as ProbeGlobal).__lifecycleProbe.demo.SendNumber(frame.endpoints.Change, 1);
    });
    assert.equal(await page.locator("#broken span").textContent(), "1");
    assert.equal(await page.locator("#duplicate section").count(), 1);
    assert.equal(await page.evaluate(() => (Reflect.get(globalThis, Symbol.for("copeland.lifecycle-demo.host")) as HostObservation[])
        .some(item => item.kind === "focus" && item.value === "missing")), false);
    observations.push("Invalid presentation and duplicate child keys roll back state, ownership, and effect launch together.");
    await page.evaluate(async () => {
        const entry = await (async (specifier: string): Promise<unknown> => import(specifier))("/lifecycle/entry.js") as { root: { unmount(): void } };
        entry.root.unmount();
        const state = (globalThis as ProbeGlobal).__lifecycleProbe;
        state.demo.SendText(state.amber.endpoints.Edit, "after root removal");
    });
    await page.evaluate(() => Promise.resolve());
    assert.equal(await page.evaluate(() => {
        const state = (globalThis as ProbeGlobal).__lifecycleProbe;
        const registry = Reflect.get(globalThis, Symbol.for("copeland.ts.react-projections.v1")) as Registry;
        return registry.instances.has(state.directory.identity) || registry.instances.has(state.amber.identity);
    }), false);
    observations.push("Root teardown retires its subtree; adapter cleanup replay does not resurrect an owner.");
    const live = await browser.newPage();
    live.on("pageerror", error => errors.push(error.message));
    await live.goto(`${server.url}/lifecycle/`);
    await live.getByLabel("Search: all, one, or none", { exact: true }).fill("one");
    await live.getByRole("region", { name: "blue", exact: true }).waitFor({ state: "detached" });
    const liveEditor = live.getByRole("region", { name: "amber", exact: true });
    await liveEditor.getByRole("textbox").fill("foreground service draft");
    await liveEditor.getByRole("button", { name: "Save", exact: true }).click();
    await liveEditor.getByText("Saved: foreground service draft", { exact: true }).waitFor();
    await live.close();
    observations.push("The standalone demonstration searches and saves through actual HTTP responses from its foreground sample service, without request interception.");
    const diagnostics = await page.evaluate(() => (Reflect.get(globalThis, Symbol.for("copeland.ts.react-projections.v1")) as Registry).diagnostics);
    assert.equal(diagnostics.filter(item => item.id === "COPE-COMPONENT-STATE-0102").length, 2);
    assert.equal(diagnostics.filter(item => item.id === "COPE-COMPONENT-STATE-0103").length, 2);
    assert.equal(diagnostics.some(item => item.message.includes("COPE-COMPONENT-CHILD-0003")), true);
    assert.deepEqual(errors, []);
    await writeFile(`${evidence}/lifecycle-${profile.toLowerCase()}-report.json`, JSON.stringify({
        milestone: "CTS-REACT-EFFECTS-CHILDREN-M7", profile, browser: browser.version(), observations, diagnostics, errors, emittedHashes,
    }, null, 2) + "\n");
    process.stdout.write(`Effects/children ${profile}: ${observations.length} mounted scenarios passed.\n`);
} finally {
    await browser.close();
    await server.close();
}
