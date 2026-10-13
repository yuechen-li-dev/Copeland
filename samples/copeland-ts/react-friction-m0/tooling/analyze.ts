import { readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../", import.meta.url));
const evidenceName = process.env.FRICTION_EVIDENCE_NAME ?? (process.env.FRICTION_STABILIZATION === "1" ? "react-ui-stabilization-m1" : "react-friction-m0");
const reportRoot = fileURLToPath(new URL(`../../../../docs/Copeland/reviews/evidence/${evidenceName}/`, import.meta.url));
const app = await readFile(`${root}/src/App.tsx`, "utf8");
const layouts = await readFile(`${root}/src/Layouts.ts`, "utf8");
const state = await readFile(`${root}/src/State.ts`, "utf8");
const main = await readFile(`${root}/src/Main.ts`, "utf8");
const owner = await readFile(`${root}/src/Application.tsx`, "utf8");
const baseline = await readFile(`${root}/baseline/App.tsx`, "utf8");
const manifest = await readFile(`${root}/manifest.tsx`, "utf8");
const issuesPage = await readFile(`${root}/src/IssuesPage.ts`, "utf8");
const issues = await readFile(`${root}/src/Issues.ts`, "utf8");
const semanticUi = await readFile(`${root}/src/UI.tsx`, "utf8");
const collections = await readFile(`${root}/src/Collections.ts`, "utf8");
const applicationSource = [app, issuesPage, issues, state, owner, main, layouts].join("\n");
const librarySource = [semanticUi, collections].join("\n");

function sourceLines(source: string) {
    return source.split(/\r?\n/).filter(line => line.trim() !== "" && !line.trim().startsWith("//")).length;
}

function intrinsicHtmlTags(source: string) {
    // Paired closing tags distinguish XML from annotations such as Option<string>.
    const candidates = source.matchAll(/<([a-z][A-Za-z0-9-]*)(?:\s[^<>]*?)?(\/?)>/g);
    return [...candidates].filter(candidate => candidate[2] === "/"
        || source.includes(`</${candidate[1]}>`)).length;
}

const report = {
    interpretation: "Source accounting for these authored specimens; not a timed usability study or a universal complexity measure.",
    sharedContent: "src/Content.ts is imported verbatim by both implementations",
    copeland: {
        appLines: sourceLines(app),
        reusableLayoutLines: sourceLines(layouts),
        stateLines: sourceLines(state),
        mainLines: sourceLines(main),
        stateOwnerLines: sourceLines(owner),
        languageStateDeclarations: (owner.match(/state:/g) ?? []).length,
        languageInputHandlers: (owner.match(/^\s*on\s+\w+\(/gm) ?? []).length,
        issuePageLines: sourceLines(issuesPage),
        issueDomainLines: sourceLines(issues),
        semanticUiLibraryLines: sourceLines(semanticUi),
        collectionLibraryLines: sourceLines(collections),
        totalApplicationLines: sourceLines(applicationSource),
        totalIncludingReusableLibraries: sourceLines(applicationSource) + sourceLines(librarySource),
        explicitCaptures: (applicationSource.match(/capture \{/g) ?? []).length,
        reusableLibraryCaptures: (librarySource.match(/capture \{/g) ?? []).length,
        issuePageIntrinsicHtmlTags: intrinsicHtmlTags(issuesPage),
        eventAdapterFunctions: (app.match(/function Send\w+\(/g) ?? []).length,
        localStreams: (app.match(/stream \w+</g) ?? []).length,
        contributionStreams: (applicationSource.match(/stream \w+ \{/g) ?? []).length,
        expressionBindings: (applicationSource.match(/bind \w+ using /g) ?? []).length,
        layoutSpecializations: (applicationSource.match(/instantiate /g) ?? []).length,
        layoutVariants: (applicationSource.match(/with \{ root\./g) ?? []).length,
        manifestLines: sourceLines(manifest),
        distinctIssueArrangements: 2,
        issueListBindingSites: (issuesPage.match(/list: IssueList/g) ?? []).length,
        issueDetailBindingSites: (issuesPage.match(/detail: IssueDetail/g) ?? []).length,
        insertedCollection: "Runtime immutable row snapshots; keyed React children within one static layout channel"
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
