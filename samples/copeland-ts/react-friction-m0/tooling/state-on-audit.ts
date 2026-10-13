import assert from "node:assert/strict";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(new URL("../baseline/package.json", import.meta.url));
const root = fileURLToPath(new URL("../", import.meta.url));
const evidenceRoot = fileURLToPath(new URL("../../../../docs/Copeland/reviews/evidence/react-state-on-audit/", import.meta.url));
interface Specimen {
    name: string;
    accepted: boolean;
    expectationMatched: boolean;
    javascript: Record<string, string> | null;
}
const report: { expectationsMatched: boolean; results: Specimen[] } = JSON.parse(
    await readFile(`${evidenceRoot}/compiler-report.json`, "utf8"));
assert.equal(report.expectationsMatched, true);
const artifactRoot = `${root}/artifacts/react-state-on-audit`;
await mkdir(artifactRoot, { recursive: true });

async function loadSpecimen(name: string, fileName: string) {
    const specimen = report.results.find(result => result.name === name);
    assert.equal(specimen?.accepted, true);
    const source = specimen?.javascript?.[fileName];
    assert.ok(source);
    // Use the installed real React package. Only its import address changes;
    // Copeland's generated state/function bodies are preserved verbatim.
    const reactUrl = pathToFileURL(require.resolve("react")).href;
    const path = `${artifactRoot}/${name}.mjs`;
    await writeFile(path, source.replace('from "react"', `from ${JSON.stringify(reactUrl)}`));
    return import(pathToFileURL(path).href);
}

const counter = await loadSpecimen("existing-component-state-and-on", "Main.js") as {
    Counter(): { props: { children: number } };
};
const first = counter.Counter();
const second = counter.Counter();
assert.equal(first.props.children, 0);
assert.equal(second.props.children, 0);
assert.deepEqual(Object.keys(counter), ["Counter"]);
const execution = await loadSpecimen("execution-flow-source-frame-api-is-admitted", "Main.js") as {
    Main(): number;
};
assert.equal(execution.Main(), 42);
await writeFile(`${evidenceRoot}/emission-observations.json`, JSON.stringify({
    reactVersion: (require("react") as { version: string }).version,
    observations: {
        ordinaryComponentCalls: [first.props.children, second.props.children],
        componentRuntimeExports: Object.keys(counter),
        executionFlowResult: execution.Main(),
    },
    interpretation: "Node invokes the actual React createElement projection. Component state reinitializes on each direct call; there is no emitted event endpoint. This is emission evidence, not mounted React lifecycle qualification. Execution FLOW start/step returns the expected value."
}, null, 2) + "\n");
process.stdout.write("Observed local state initialization, absent runtime event endpoint, and typed execution FLOW result.\n");
