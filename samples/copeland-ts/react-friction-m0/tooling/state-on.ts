import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { startServer } from "./server.ts";

interface ReactElement {
    props: { "aria-label"?: string; children?: ReactElement[]; onClick?: () => void };
}
interface Projection {
    identity: string;
    endpoints: Record<string, unknown>;
    trace: { kind: string; revision: number }[];
    getSnapshot(): { state: unknown; presentation: ReactElement; revision: number };
}
interface Registry {
    instances: Map<string, Projection>;
    diagnostics: { id: string; componentInstanceId: string }[];
}
interface ProbeModule {
    Counter(seed: number, label: string): unknown;
    Machine(limit: number): unknown;
    Checked(): unknown;
    StackMachine(value: number, label: string): unknown;
    OverflowMachine(): unknown;
    ReadOnly(seed: number): unknown;
    List(): unknown;
    Fire(endpoint: unknown, value: number): void;
}
interface Root {
    render(node: unknown): void;
    unmount(): void;
}
interface ProbeState {
    probe: ProbeModule;
    first: Root;
    renderFirst(seed: number): void;
    second: Root;
    readOnly: Root;
    firstOwner: Projection;
    secondOwner: Projection;
    checkedOwner: Projection;
    snapshot: () => void;
}
type ProbeGlobal = typeof globalThis & { __stateOnProbe: ProbeState };

const require = createRequire(new URL("../baseline/package.json", import.meta.url));
const { chromium } = require("playwright") as typeof import("../baseline/node_modules/playwright/index.js");
const fixture = fileURLToPath(new URL("../", import.meta.url));
const evidenceName = process.env.FRICTION_EVIDENCE_NAME ?? "react-state-on-m6";
const evidence = fileURLToPath(new URL(`../../../../docs/Copeland/reviews/evidence/${evidenceName}/`, import.meta.url));
const report = JSON.parse(await readFile(`${evidence}/compiler-report.json`, "utf8"));
const profile = process.env.FRICTION_COMPONENT_PROFILE ?? "Diagnostic";
const specimenName = profile === "Diagnostic" ? "state-on-lifetime-projection" : `state-on-lifetime-${profile.toLowerCase()}`;
const specimen = report.results.find((result: { name: string }) => result.name === specimenName);
assert.equal(specimen?.expectationMatched, true);
const artifact = `${fixture}/artifacts/${evidenceName}/state-on`;
await mkdir(artifact, { recursive: true });
const assets: Record<string, string> = {};
const emittedHashes: Record<string, string> = {};
for (const [name, source] of Object.entries(specimen.javascript as Record<string, string>)) {
    const path = `${artifact}/${name}`;
    await writeFile(path, source);
    assets[`/state-on/${name}`] = path;
    emittedHashes[name] = createHash("sha256").update(source).digest("hex");
}
const server = await startServer(0, undefined, assets);
let browser: import("../baseline/node_modules/playwright/index.js").Browser | undefined;
const errors: string[] = [];
const observations: string[] = [];
try {
    browser = await chromium.launch({ channel: process.env.FRICTION_BROWSER_CHANNEL ?? "msedge", headless: true });
    const page = await browser.newPage();
    page.on("pageerror", error => errors.push(error.stack ?? error.message));
    page.on("console", message => {
        if (message.type() === "error" || message.type() === "warning") errors.push(message.text());
    });
    await page.goto(server.url);
    await page.locator("nav").waitFor();
    await page.evaluate(async () => {
        const load = (specifier: string): Promise<unknown> => import(specifier);
        const ReactDOM = await load("react-dom/client") as { createRoot(host: Element): Root };
        const React = await load("react") as { StrictMode: unknown; createElement(type: unknown, props: unknown, child: unknown): unknown };
        const probe = await load("/state-on/Probe.js") as ProbeModule;
        function root(id: string): Root {
            const host = document.createElement("div");
            host.id = id;
            document.body.appendChild(host);
            return ReactDOM.createRoot(host);
        }
        const first = root("state-first");
        const second = root("state-second");
        const renderFirst = (seed: number) => first.render(React.createElement(React.StrictMode, null, probe.Counter(seed, "First")));
        renderFirst(0);
        second.render(probe.Counter(10, "Second"));
        root("state-machine").render(probe.Machine(2));
        root("state-checked").render(probe.Checked());
        root("state-stack-success").render(probe.StackMachine(1, "Stack success"));
        root("state-stack-failure").render(probe.StackMachine(-2, "Stack failure"));
        root("state-overflow").render(probe.OverflowMachine());
        const readOnly = root("state-readonly");
        readOnly.render(probe.ReadOnly(5));
        root("state-list").render(probe.List());
        (globalThis as ProbeGlobal).__stateOnProbe = { probe, first, second, renderFirst, readOnly } as ProbeState;
    });
    await page.getByRole("region", { name: "First", exact: true }).getByRole("button", { name: "Add", exact: true }).waitFor();
    await page.getByRole("region", { name: "Checked", exact: true }).waitFor();
    await page.evaluate(() => {
        const registry = Reflect.get(globalThis, Symbol.for("copeland.ts.react-projections.v1")) as Registry;
        const state = (globalThis as ProbeGlobal).__stateOnProbe;
        const owner = (label: string) => {
            const result = [...registry.instances.values()].find(instance => instance.getSnapshot().presentation.props["aria-label"] === label);
            if (!result) throw new Error(`Missing mounted owner: ${label}`);
            return result;
        };
        state.firstOwner = owner("First");
        state.secondOwner = owner("Second");
        state.checkedOwner = owner("Checked");
        state.snapshot = state.firstOwner.getSnapshot().presentation.props.children![2].props.onClick!;
        state.probe.Fire(state.firstOwner.endpoints.Add, 1);
        state.probe.Fire(state.firstOwner.endpoints.Add, 1);
    });
    const count = (id: string) => page.locator(`#${id} span`).first().textContent();
    await page.waitForFunction(() => document.querySelector("#state-first span")?.textContent === "2");
    assert.equal(await count("state-second"), "10");
    observations.push("Two inputs in one browser turn read the latest committed state; independent owners remain isolated.");
    await page.evaluate(() => {
        const state = (globalThis as ProbeGlobal).__stateOnProbe;
        state.renderFirst(99);
        state.readOnly.render(state.probe.ReadOnly(99));
    });
    await page.evaluate(() => new Promise<void>(done => requestAnimationFrame(() => requestAnimationFrame(() => done()))));
    assert.equal(await count("state-first"), "2");
    assert.equal(await count("state-readonly"), "5");
    observations.push("StrictMode effect replay and changed render inputs preserve the owner state instead of reinitializing it.");
    await page.evaluate(() => (globalThis as ProbeGlobal).__stateOnProbe.snapshot());
    await page.waitForFunction(() => document.querySelector("#state-first span")?.textContent === "0");
    observations.push("An explicitly captured state record remains the original immutable snapshot.");
    const tick = page.getByRole("region", { name: "Machine", exact: true }).getByRole("button", { name: "Tick" });
    await tick.click();
    assert.equal(await count("state-machine"), "1");
    await tick.click();
    assert.equal(await count("state-machine"), "2");
    await tick.click();
    await tick.click();
    assert.equal(await count("state-machine"), "2");
    observations.push("FLOW progresses exactly one explicit step per Tick, reaches completion, and remains completed.");
    for (const [label, id, result] of [["Stack success", "state-stack-success", "11"], ["Stack failure", "state-stack-failure", "-2"]]) {
        const next = page.getByRole("region", { name: label, exact: true }).getByRole("button", { name: "Tick" });
        assert.equal(await count(id), "1");
        await next.click();
        assert.equal(await count(id), "2");
        await next.click();
        assert.equal(await count(id), "1");
        await next.click();
        assert.equal(await page.locator(`#${id} span`).nth(1).textContent(), result);
    }
    const overflowTick = page.getByRole("region", { name: "Overflow", exact: true }).getByRole("button", { name: "Tick" });
    await overflowTick.click();
    assert.equal(await count("state-overflow"), "2");
    await overflowTick.click();
    assert.equal(await page.locator("#state-overflow span").nth(1).textContent(), "1");
    await overflowTick.click();
    assert.equal(await page.locator("#state-overflow span").nth(1).textContent(), "1");
    observations.push("Typed child push/pop retains success and failure continuations; bounded recursive overflow remains faulted.");
    const rollback = await page.evaluate(() => {
        const state = (globalThis as ProbeGlobal).__stateOnProbe;
        const before = state.checkedOwner.getSnapshot();
        state.probe.Fire(state.checkedOwner.endpoints.Set, 1);
        const after = state.checkedOwner.getSnapshot();
        return { unchanged: before === after, revision: after.revision, state: after.state };
    });
    assert.deepEqual(rollback, { unchanged: true, revision: 0, state: 0 });
    const invalidPayload = await page.evaluate(async () => {
        const load = (specifier: string): Promise<unknown> => import(specifier);
        const host = await load("@copeland/browser-v1") as { dispatchComponentEvent(id: string, name: string, payload: unknown): unknown };
        const state = (globalThis as ProbeGlobal).__stateOnProbe;
        try {
            host.dispatchComponentEvent(state.firstOwner.identity, "Add", ["bad"]);
            return "unexpected acceptance";
        } catch (error) {
            return String(error);
        }
    });
    assert.match(invalidPayload, /int|integer|number/i);
    const invalidValues = await page.evaluate(async () => {
        const load = (specifier: string): Promise<unknown> => import(specifier);
        const host = await load("@copeland/browser-v1") as { dispatchComponentEvent(id: string, name: string, payload: unknown): unknown };
        const registry = Reflect.get(globalThis, Symbol.for("copeland.ts.react-projections.v1")) as Registry;
        const owner = [...registry.instances.values()].find(instance => instance.getSnapshot().presentation.props["aria-label"] === "List")!;
        const failures: string[] = [];
        for (const values of [[1.5], [2147483648], ["bad"], new Array(1)]) {
            try {
                host.dispatchComponentEvent(owner.identity, "Set", [Object.freeze(values)]);
                failures.push("unexpected acceptance");
            } catch (error) {
                failures.push(String(error));
            }
        }
        try {
            host.dispatchComponentEvent(owner.identity, "Set", [[4]]);
            failures.push("unexpected acceptance");
        } catch (error) {
            failures.push(String(error));
        }
        host.dispatchComponentEvent(owner.identity, "Set", [Object.freeze([4])]);
        return failures;
    });
    assert.equal(invalidValues.length, 5);
    for (const failure of invalidValues) assert.match(failure, /int/i);
    await page.waitForFunction(() => document.querySelector("#state-list span")?.textContent === "4");
    assert.equal(await count("state-checked"), "7");
    assert.equal(await count("state-first"), "0");
    observations.push("Failed presentation and malformed typed payload leave canonical state and prior presentation unchanged.");
    await page.evaluate(() => {
        const state = (globalThis as ProbeGlobal).__stateOnProbe;
        state.first.unmount();
        state.probe.Fire(state.firstOwner.endpoints.Add, 1);
        state.probe.Fire(state.secondOwner.endpoints.Add, 2);
    });
    await page.waitForFunction(() => document.querySelector("#state-second span")?.textContent === "12");
    assert.equal(await page.locator("#state-first").textContent(), "");
    const lifecycle = await page.evaluate(async () => {
        const state = (globalThis as ProbeGlobal).__stateOnProbe;
        const registry = Reflect.get(globalThis, Symbol.for("copeland.ts.react-projections.v1")) as Registry;
        const load = (specifier: string): Promise<unknown> => import(specifier);
        const host = await load("@copeland/browser-v1") as { inspectComponentFrameTrace(id: string): unknown };
        return {
            removed: !registry.instances.has(state.firstOwner.identity),
            diagnostics: registry.diagnostics,
            projectionTrace: state.secondOwner.trace,
            canonicalTrace: host.inspectComponentFrameTrace(state.firstOwner.identity),
        };
    });
    assert.equal(lifecycle.removed, true);
    assert.deepEqual(lifecycle.diagnostics.map(diagnostic => diagnostic.id), ["COPE-COMPONENT-STATE-0102", "COPE-COMPONENT-STATE-0103"]);
    assert.ok(lifecycle.projectionTrace.some(event => event.kind === "PresentationCommitted" && event.revision === 1));
    observations.push("Unmount destroys the canonical frame and invalidates retained endpoints; another owner continues and acknowledges React commit.");
    assert.deepEqual(errors, []);
    const reportName = profile === "Diagnostic" ? "state-on-browser-report.json" : `state-on-${profile.toLowerCase()}-browser-report.json`;
    await writeFile(`${evidence}/${reportName}`, JSON.stringify({
        milestone: "CTS-REACT-STATE-ON-M6", profile, browser: browser.version(), sourceHashes: specimen.sourceHashes,
        emittedHashes, observations, rollback, invalidPayload, invalidValues, lifecycle, errors,
    }, null, 2) + "\n");
    process.stdout.write(`State/on ${profile}: ${observations.length} mounted browser scenarios passed.\n`);
} finally {
    await browser?.close();
    await server.close();
}
