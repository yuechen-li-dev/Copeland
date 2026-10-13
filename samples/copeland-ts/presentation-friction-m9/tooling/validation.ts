import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const repository = fileURLToPath(new URL("../../../../", import.meta.url));
const evidence = "docs/Copeland/reviews/evidence/presentation-friction-m9/";

async function hash(path: string): Promise<string> {
    return createHash("sha256")
        .update(await readFile(repository + path))
        .digest("hex");
}

const buildPath = "artifacts/validation/presentation-friction-m9-build.log";
const testsPath = "artifacts/validation/presentation-friction-m9-tests.log";
const build = await readFile(repository + buildPath, "utf8");
const tests = await readFile(repository + testsPath, "utf8");
assert.match(build, /Build succeeded\./);
assert.match(build, /0 Warning\(s\)/);
assert.match(build, /0 Error\(s\)/);
const suites = [
    ...tests.matchAll(
        /Passed!\s+- Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+), Total:\s+(\d+),.* - ([^\s]+\.dll)/g,
    ),
].map((match) => ({
    assembly: match[5],
    failed: Number(match[1]),
    passed: Number(match[2]),
    skipped: Number(match[3]),
    total: Number(match[4]),
}));
assert.equal(suites.length, 8, "Expected all solution test assemblies");
for (const suite of suites) {
    assert.equal(suite.failed, 0);
    assert.equal(suite.skipped, 0);
    assert.equal(suite.passed, suite.total);
}
const browserPath = evidence + "browser-report.json";
const browser = JSON.parse(await readFile(repository + browserPath, "utf8"));
assert.equal(browser.errors.length, 0);
assert.equal(browser.observations.length, 192);
const hashes: Record<string, string> = {};
for (const path of [
    buildPath,
    testsPath,
    browserPath,
    evidence + "compiler-report.json",
    "samples/copeland-ts/presentation-friction-m9/package-lock.json",
    "samples/copeland-ts/react-friction-m0/dist/browser/packages/copeland-browser-v1/index.js",
    "src/Copeland/Copeland.TS/bin/Debug/net10.0/Copeland.TS.dll",
    "src/Copeland/Copeland.TS.Backend.JavaScript/bin/Debug/net10.0/Copeland.TS.Backend.JavaScript.dll",
]) {
    hashes[path] = await hash(path);
}
await writeFile(
    repository + evidence + "validation.json",
    JSON.stringify(
        {
            milestone: "CTS-PRESENTATION-FRICTION-M9",
            build: { warnings: 0, errors: 0 },
            tests: {
                passed: suites.reduce(
                    (total, suite) => total + suite.passed,
                    0,
                ),
                failed: 0,
                skipped: 0,
                suites,
            },
            presentationBrowser: {
                observations: browser.observations.length,
                errors: browser.errors,
                version: browser.browser,
            },
            hashes,
            boundary:
                "One shared View source, React/Vue targets, typed controls and canonical ownership. Mixed-framework islands remain a separate host-boundary milestone.",
        },
        null,
        2,
    ) + "\n",
);
