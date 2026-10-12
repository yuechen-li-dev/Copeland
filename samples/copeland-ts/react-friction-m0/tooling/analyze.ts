import { readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../", import.meta.url));
const evidenceName = process.env.FRICTION_STABILIZATION === "1" ? "react-ui-stabilization-m1" : "react-friction-m0";
const reportRoot = fileURLToPath(new URL(`../../../../docs/Copeland/reviews/evidence/${evidenceName}/`, import.meta.url));
const app = await readFile(`${root}/src/App.tsx`, "utf8");
const state = await readFile(`${root}/src/State.ts`, "utf8");
const main = await readFile(`${root}/src/Main.ts`, "utf8");
const baseline = await readFile(`${root}/baseline/App.tsx`, "utf8");
const manifest = await readFile(`${root}/manifest.tsx`, "utf8");

function sourceLines(source: string) {
    return source.split(/\r?\n/).filter(line => line.trim() !== "" && !line.trim().startsWith("//")).length;
}

const report = {
    interpretation: "Source accounting for these authored specimens; not a timed usability study or a universal complexity measure.",
    sharedContent: "src/Content.ts is imported verbatim by both implementations",
    copeland: {
        appLines: sourceLines(app),
        stateLines: sourceLines(state),
        mainLines: sourceLines(main),
        totalApplicationLines: sourceLines(app) + sourceLines(state) + sourceLines(main),
        explicitCaptures: (app.match(/capture \{/g) ?? []).length + (main.match(/capture \{/g) ?? []).length,
        eventAdapterFunctions: (app.match(/function Send\w+\(/g) ?? []).length,
        localStreams: (app.match(/stream \w+</g) ?? []).length,
        manifestLines: sourceLines(manifest),
        distinctIssueArrangements: 2,
        issueListBindingSites: (app.match(/list: IssueList/g) ?? []).length,
        issueDetailBindingSites: (app.match(/detail: IssueDetail/g) ?? []).length,
        insertedCollection: "Bounded fourth row selected by state; no general dynamic collection capability"
    },
    typescript: {
        totalApplicationLines: sourceLines(baseline),
        explicitCaptures: 0,
        eventAdapterFunctions: 0,
        issueListBindingSites: (baseline.match(/<IssueList /g) ?? []).length,
        issueDetailBindingSites: (baseline.match(/<IssueDetail /g) ?? []).length,
        insertedCollection: "Derived array filtered and mapped with stable issue keys"
    }
};

await writeFile(`${reportRoot}/authoring-accounting.json`, JSON.stringify(report, null, 2) + "\n");
process.stdout.write(JSON.stringify(report, null, 2) + "\n");
