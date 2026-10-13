import assert from "node:assert/strict";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { chromium, type Page } from "playwright";
import { startServer } from "./server.ts";

const evidence = fileURLToPath(
    new URL(
        "../../../../docs/Copeland/reviews/evidence/mixed-islands-m10/",
        import.meta.url,
    ),
);
await mkdir(evidence, { recursive: true });
const server = await startServer();
const browser = await chromium.launch({
    channel: process.env.FRICTION_BROWSER_CHANNEL ?? "msedge",
});
const errors: string[] = [];
const observations: object[] = [];

async function settled(page: Page) {
    await page.waitForFunction(() => {
        const registry = Reflect.get(
            globalThis,
            Symbol.for("copeland.ts.view-projections.v1"),
        );
        return (
            registry?.instances.size === 4 &&
            [...registry.instances.values()].every((frame: any) =>
                frame.trace.some(
                    (event: any) =>
                        event.kind === "PresentationCommitted" &&
                        event.revision === frame.getSnapshot().revision,
                ),
            )
        );
    });
}

async function qualify(target: string, width: number) {
    const page = await browser.newPage({ viewport: { width, height: 1100 } });
    page.on("pageerror", (error) => errors.push(error.stack ?? error.message));
    page.on("console", (message) => {
        if (message.type() === "warning" || message.type() === "error")
            errors.push(message.text());
    });
    page.on("response", (response) => {
        if (response.status() >= 400)
            errors.push(`${response.status()} ${response.url()}`);
    });
    function record(name: string) {
        observations.push({ target, width, name });
    }
    const vue = page.locator('section[aria-label="vue"]');
    const react = page.locator('section[aria-label="react"]');
    await page.goto(server.url + `/${target}/`);
    await settled(page);
    assert.equal(await page.locator('[data-framework="react"]').count(), 2);
    assert.equal(await page.locator('[data-framework="vue"]').count(), 2);
    record("both-genuine-framework-roots-mounted");
    await page.locator("#mirror-react").fill("One shared state");
    await settled(page);
    assert.equal(
        await page.locator("#mirror-vue").inputValue(),
        "One shared state",
    );
    await page.locator("#mirror-vue").fill("Vue updates both");
    await settled(page);
    assert.equal(
        await page.locator("#mirror-react").inputValue(),
        "Vue updates both",
    );
    record("both-frameworks-edit-one-canonical-state");
    await page.evaluate(() => {
        const inspection = Reflect.get(globalThis, "__islandInspection");
        inspection.held = true;
        Reflect.set(
            globalThis,
            "__sharedCommitStart",
            inspection.commits.length,
        );
    });
    await page
        .getByRole("button", { name: "Reveal shared and focus", exact: true })
        .click();
    await page.waitForFunction(() => {
        const inspection = Reflect.get(globalThis, "__islandInspection");
        const commits = inspection.commits.slice(
            Reflect.get(globalThis, "__sharedCommitStart"),
        );
        return (
            commits.some((entry: any) => entry.id === "mirror-react") &&
            commits.some((entry: any) => entry.id === "mirror-vue")
        );
    });
    await page.evaluate(() => {
        const inspection = Reflect.get(globalThis, "__islandInspection");
        const commits = inspection.commits.slice(
            Reflect.get(globalThis, "__sharedCommitStart"),
        );
        commits
            .filter((entry: any) => entry.id === "mirror-react")
            .at(-1)
            .commit();
        const registry = Reflect.get(
            globalThis,
            Symbol.for("copeland.ts.view-projections.v1"),
        );
        const shared = [...registry.instances.values()].find((frame: any) =>
            frame.identity.includes('["shared","mirror"]'),
        );
        if (
            shared.trace.some(
                (entry: any) =>
                    entry.kind === "PresentationCommitted" &&
                    entry.revision === shared.getSnapshot().revision,
            )
        )
            throw new Error("One participant released a two-island effect.");
        commits
            .filter((entry: any) => entry.id === "mirror-vue")
            .at(-1)
            .commit();
        inspection.held = false;
    });
    await page.waitForFunction(
        () => document.activeElement?.id === "mirror-vue",
    );
    record("shared-owner-effect-waits-for-both-framework-commits");
    await page.locator("#react").fill("React owned draft");
    await page.locator("#vue").fill("Vue owned draft");
    await settled(page);
    await react
        .getByRole("button", { name: "React local: 0", exact: true })
        .click();
    await vue
        .getByRole("button", { name: "Vue local: 0", exact: true })
        .click();
    await page.evaluate(() => {
        const registry = Reflect.get(
            globalThis,
            Symbol.for("copeland.ts.view-projections.v1"),
        );
        const hosts = [
            ...document.querySelectorAll(
                '[aria-label="Editors"] [data-island-host]',
            ),
        ];
        const frames = [...registry.instances.values()].filter((frame: any) =>
            frame.identity.includes("::child::"),
        );
        Reflect.set(globalThis, "__proof", {
            hosts,
            roots: hosts.map((host) => host.firstElementChild),
            inputs: [
                document.getElementById("react"),
                document.getElementById("vue"),
            ],
            frames,
            oldVue: frames.find((frame: any) =>
                frame.identity.includes('["editors","vue"]'),
            ),
            callbacks: Reflect.get(
                globalThis,
                "__islandInspection",
            ).callbacks.slice(),
        });
    });
    record("typed-events-update-canonical-drafts");
    await page
        .getByRole("button", { name: "Reverse islands", exact: true })
        .click();
    await settled(page);
    assert.deepEqual(
        await page
            .locator('[aria-label="Editors"] [data-island-host]')
            .evaluateAll((hosts) =>
                hosts.map((host) => host.getAttribute("data-island-host")),
            ),
        ["vue", "react"],
    );
    await page.evaluate(() => {
        const proof = Reflect.get(globalThis, "__proof");
        for (let index = 0; index < proof.hosts.length; index += 1) {
            if (
                !proof.hosts[index].isConnected ||
                proof.hosts[index].firstElementChild !== proof.roots[index] ||
                !proof.inputs[index].isConnected
            )
                throw new Error("Reorder replaced an island root.");
        }
    });
    assert.equal(await page.locator("#vue").inputValue(), "Vue owned draft");
    await react
        .getByRole("button", { name: "React local: 1", exact: true })
        .waitFor();
    await vue
        .getByRole("button", { name: "Vue local: 1", exact: true })
        .waitFor();
    record("reorder-preserves-host-root-draft-and-foreign-local-state");
    await page
        .getByRole("button", { name: "Update labels", exact: true })
        .click();
    await settled(page);
    await react.getByRole("heading", { name: "Updated React" }).waitFor();
    await vue.getByRole("heading", { name: "Updated Vue" }).waitFor();
    record("typed-props-update-existing-island-roots");
    await page.evaluate(() => {
        for (const callback of Reflect.get(globalThis, "__proof").callbacks)
            callback.change("Stale callback write");
    });
    assert.equal(await page.locator("#vue").inputValue(), "Vue owned draft");
    assert.equal(
        await page.locator("#react").inputValue(),
        "React owned draft",
    );
    record("superseded-revision-callbacks-cannot-write-state");

    await page.evaluate(() => {
        const inspection = Reflect.get(globalThis, "__islandInspection");
        inspection.held = true;
        Reflect.set(globalThis, "__commitStart", inspection.commits.length);
    });
    await vue
        .getByRole("button", { name: "Reveal and focus", exact: true })
        .click();
    await page.waitForFunction(() => {
        const inspection = Reflect.get(globalThis, "__islandInspection");
        return inspection.commits
            .slice(Reflect.get(globalThis, "__commitStart"))
            .some((entry: any) => entry.id === "vue");
    });
    assert.equal(await page.locator("#vue").inputValue(), "Presented vue");
    await page.evaluate(() => {
        const frame = Reflect.get(globalThis, "__proof").oldVue;
        if (
            frame.trace.some(
                (entry: any) =>
                    entry.kind === "PresentationCommitted" &&
                    entry.revision === frame.getSnapshot().revision,
            )
        )
            throw new Error(
                "Outer commit acknowledged pending foreign presentation.",
            );
        Reflect.set(globalThis, "__heldRevision", frame.getSnapshot().revision);
    });
    record("presentation-effect-waits-for-foreign-commit");
    await vue
        .getByRole("button", { name: "Reveal and focus", exact: true })
        .click();
    await page.waitForFunction(
        () =>
            Reflect.get(globalThis, "__proof").oldVue.getSnapshot().revision >
            Reflect.get(globalThis, "__heldRevision"),
    );
    await page.waitForFunction(() => {
        const inspection = Reflect.get(globalThis, "__islandInspection");
        return (
            inspection.commits
                .slice(Reflect.get(globalThis, "__commitStart"))
                .filter((entry: any) => entry.id === "vue").length >= 2
        );
    });
    await page.evaluate(() => {
        const inspection = Reflect.get(globalThis, "__islandInspection");
        const commits = inspection.commits
            .slice(Reflect.get(globalThis, "__commitStart"))
            .filter((entry: any) => entry.id === "vue");
        commits[0].commit();
        const frame = Reflect.get(globalThis, "__proof").oldVue;
        if (
            frame.trace.some(
                (entry: any) =>
                    entry.kind === "PresentationCommitted" &&
                    entry.revision === frame.getSnapshot().revision,
            )
        )
            throw new Error(
                "Stale foreign commit acknowledged newer revision.",
            );
        commits.at(-1).commit();
        inspection.held = false;
    });
    await page.waitForFunction(() => document.activeElement?.id === "vue");
    const focusCount = await page.evaluate(() => {
        const events = Reflect.get(
            globalThis,
            Symbol.for("copeland.lifecycle-demo.host"),
        );
        return events.filter(
            (event: any) => event.kind === "focus" && event.value === "vue",
        ).length;
    });
    assert.equal(focusCount, 1);
    record("only-exact-revision-commit-releases-focus-effect");

    let releaseSave: (() => void) | undefined;
    const saveGate = new Promise<void>((resolve) => {
        releaseSave = resolve;
    });
    await page.route("**/lifecycle-api/save?**", async (route) => {
        await saveGate;
        await route.fulfill({
            contentType: "application/json",
            body: JSON.stringify({ ok: true, value: "Retired completion" }),
        });
    });
    try {
        const requested = page.waitForRequest((request) =>
            request.url().includes("/lifecycle-api/save"),
        );
        await vue.getByRole("button", { name: "Save", exact: true }).click();
        await requested;
        await page
            .getByRole("button", { name: "Remove Vue", exact: true })
            .click();
        await page.locator("#vue").waitFor({ state: "detached" });
        await page.evaluate(() => {
            const proof = Reflect.get(globalThis, "__proof");
            const runtime = Reflect.get(globalThis, "__mixedDemo").runtime;
            const retired = proof.oldVue.trace.filter(
                (entry: any) => entry.kind === "PresentationHostRetired",
            );
            if (retired.length !== 1)
                throw new Error("Foreign host did not retire exactly once.");
            const counts = runtime.inspectAttachmentRuntime(retired[0].effect);
            if (counts.mounted || counts.mounts !== counts.unmounts)
                throw new Error(JSON.stringify(counts));
        });
        record("removal-releases-island-exactly-once");
        await page
            .getByRole("button", { name: "Restore Vue", exact: true })
            .click();
        await settled(page);
        assert.equal(await page.locator("#vue").inputValue(), "vue draft");
        await vue
            .getByRole("button", { name: "Vue local: 0", exact: true })
            .waitFor();
        await page.evaluate(() => {
            const registry = Reflect.get(
                globalThis,
                Symbol.for("copeland.ts.view-projections.v1"),
            );
            const fresh = [...registry.instances.values()].find((frame: any) =>
                frame.identity.includes('["editors","vue"]'),
            );
            if (fresh === Reflect.get(globalThis, "__proof").oldVue)
                throw new Error("Reinserted owner reused retired generation.");
            for (const callback of Reflect.get(globalThis, "__proof").callbacks)
                callback.change("Retired callback write");
        });
        record("reinsertion-has-fresh-owner-and-foreign-local-state");
        const responded = page.waitForResponse((response) =>
            response.url().includes("/lifecycle-api/save"),
        );
        releaseSave!();
        await responded;
        await page.waitForFunction(() =>
            Reflect.get(globalThis, "__proof").oldVue.trace.some(
                (entry: any) => entry.kind === "EffectCompletionDiscarded",
            ),
        );
        assert.equal(await page.locator("#vue").inputValue(), "vue draft");
        assert.equal(
            await page
                .getByText("Retired completion", { exact: false })
                .count(),
            0,
        );
        record("retired-callback-and-http-completion-cannot-reach-new-owner");
    } finally {
        releaseSave?.();
    }
    await page.evaluate(() => {
        const inspection = Reflect.get(globalThis, "__islandInspection");
        for (const commit of inspection.commits) commit.commit();
    });
    assert.equal(await page.locator("#vue").inputValue(), "vue draft");
    record("retired-generation-acknowledgements-are-ignored");
    const geometry = await page.evaluate(() => {
        const hosts = [...document.querySelectorAll("[data-island-host]")];
        return {
            overflow: document.documentElement.scrollWidth > innerWidth + 1,
            styles: hosts.map((host) => getComputedStyle(host).fontSize),
        };
    });
    assert.equal(geometry.overflow, false);
    assert.deepEqual(geometry.styles, ["18px", "18px", "18px", "18px"]);
    record("copeland-style-reaches-light-dom-hosts-without-overflow");
    await page.screenshot({
        path: `${evidence}/${target.replaceAll("/", "-")}-${width}.png`,
        fullPage: true,
    });
    await page.evaluate(() => {
        const registry = Reflect.get(
            globalThis,
            Symbol.for("copeland.ts.view-projections.v1"),
        );
        Reflect.set(globalThis, "__allFrames", [
            ...registry.instances.values(),
        ]);
        Reflect.get(globalThis, "__mixedDemo").unmount();
    });
    await page.waitForFunction(
        () =>
            Reflect.get(
                globalThis,
                Symbol.for("copeland.ts.view-projections.v1"),
            ).instances.size === 0,
    );
    await page.evaluate(() => {
        const registry = Reflect.get(
            globalThis,
            Symbol.for("copeland.ts.view-projections.v1"),
        );
        if (registry.diagnostics.length !== 0)
            throw new Error(JSON.stringify(registry.diagnostics));
        const runtime = Reflect.get(globalThis, "__mixedDemo").runtime;
        for (const frame of Reflect.get(globalThis, "__allFrames")) {
            for (const event of frame.trace.filter(
                (entry: any) => entry.kind === "PresentationHostRetired",
            )) {
                const counts = runtime.inspectAttachmentRuntime(event.effect);
                if (counts.mounted || counts.mounts !== counts.unmounts)
                    throw new Error(
                        "Unbalanced canonical attachment lifetime.",
                    );
            }
        }
    });
    record("root-teardown-retires-all-frames-and-attachments");
    await page.close();
}

try {
    for (const adapter of ["react", "vue"]) {
        for (const profile of ["diagnostic", "symbolic", "production"]) {
            for (const width of [1280, 768, 390])
                await qualify(`${adapter}/${profile}`, width);
        }
    }
    assert.deepEqual(errors, []);
    await writeFile(
        evidence + "/browser-report.json",
        JSON.stringify(
            {
                milestone: "CTS-MIXED-ISLANDS-M10",
                browser: browser.version(),
                errors,
                observations,
                compiler: JSON.parse(
                    await readFile(evidence + "/compiler-report.json", "utf8"),
                ),
            },
            null,
            2,
        ) + "\n",
    );
    console.log(
        `Passed ${observations.length} mixed-island observations with no browser diagnostics.`,
    );
} finally {
    await browser.close();
    await server.close();
}
