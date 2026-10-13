import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../../../", import.meta.url));
const evidence = `${root}/docs/Copeland/reviews/evidence/react-effects-children-m7`;
const buildLog = "artifacts/validation/react-effects-children-m7-build.log";
const testLog = "artifacts/validation/react-effects-children-m7-tests.log";
const build = await readFile(`${root}/${buildLog}`, "utf8");
const tests = await readFile(`${root}/${testLog}`, "utf8");
assert.match(build, /Build succeeded/);
assert.match(build, /0 Warning/);
assert.match(build, /0 Error/);
const suites = [...tests.matchAll(/Test run for ([^\r\n]+?\.dll)[\s\S]*?Passed!\s+- Failed:\s*(\d+), Passed:\s*(\d+), Skipped:\s*(\d+), Total:\s*(\d+)/g)]
    .map(match => ({ assembly: match[1].split(/[\\/]/).pop(), failed: Number(match[2]), passed: Number(match[3]), skipped: Number(match[4]), total: Number(match[5]) }));
assert.equal(suites.length, 8);
assert.equal(suites.reduce((sum, suite) => sum + suite.failed, 0), 0);
const compiler = JSON.parse(await readFile(`${evidence}/compiler-report.json`, "utf8"));
assert.equal(compiler.results.every((result: { expectationMatched: boolean }) => result.expectationMatched), true);
const lifecycle = [];
const hashes: Record<string, string> = {};
for (const profile of ["diagnostic", "symbolic", "production"]) {
    const file = `lifecycle-${profile}-report.json`;
    const text = await readFile(`${evidence}/${file}`, "utf8");
    const report = JSON.parse(text);
    lifecycle.push({ profile: report.profile, passed: report.observations.length, browser: report.browser, errors: report.errors });
    hashes[file] = createHash("sha256").update(text).digest("hex");
}
for (const file of ["compiler-report.json", "browser-report.json", "semantic-library-report.json", "state-on-browser-report.json", "state-on-symbolic-browser-report.json", "state-on-production-browser-report.json"]) {
    hashes[file] = createHash("sha256").update(await readFile(`${evidence}/${file}`)).digest("hex");
}
const validation = {
    milestone: "CTS-REACT-EFFECTS-CHILDREN-M7",
    status: "success-qualified-event-effects-and-keyed-children",
    reproduction: "./samples/copeland-ts/react-friction-m0/run.ps1 -FullValidation",
    build: { command: "dotnet build Copeland.slnx -m:1 -v:minimal", warnings: 0, errors: 0, log: buildLog, sha256: createHash("sha256").update(build).digest("hex") },
    tests: { command: "dotnet test Copeland.slnx --no-build -m:1", passed: suites.reduce((sum, suite) => sum + suite.passed, 0), suites, log: testLog, sha256: createHash("sha256").update(tests).digest("hex") },
    compilerExpectations: compiler.results.length,
    lifecycle,
    evidenceHashes: hashes,
    boundaries: ["Synchronous client React profile with StrictMode replay", "AttachmentsSettled requires a future participant acknowledgement bridge", "Concurrent rendering, Suspense, SSR and retained/reparented children are unqualified", "External delivery is not exactly-once; cancellation does not undo remote work", "Legacy Zig skipped: its compiler and build infrastructure did not change"],
};
await writeFile(`${evidence}/validation.json`, JSON.stringify(validation, null, 2) + "\n");
process.stdout.write(`M7 closeout: ${validation.tests.passed} .NET tests; ${compiler.results.length} compiler expectations; ${lifecycle.map(result => result.passed).join("/")} lifecycle observations.\n`);
