import { deepStrictEqual, equal, ok } from "assert/strict";
import { execFileSync } from "child_process";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";

const engine = path.resolve(__dirname, "../../Copeland.Cli/MigrationReport.cjs");

suite("Migration report", () => {
    let root: string;

    setup(() => {
        root = fs.mkdtempSync(path.join(os.tmpdir(), "copeland-migration-"));
        write("node_modules/typescript/index.js", "module.exports = require(" +
            JSON.stringify(require.resolve("typescript")) + ");");
    });

    teardown(() => {
        fs.rmSync(root, { recursive: true, force: true });
    });

    function write(relative: string, contents: string): void {
        const file = path.join(root, relative);
        fs.mkdirSync(path.dirname(file), { recursive: true });
        fs.writeFileSync(file, contents);
    }

    function run(project: string, proposedFiles?: string[]): string {
        return execFileSync(process.execPath, [engine], {
            cwd: root,
            encoding: "utf8",
            input: JSON.stringify({ project: path.join(root, project), proposedFiles }),
        });
    }

    test("reports resolved local imports and source spans without editing the legacy project", () => {
        write("tsconfig.json", '{"include":["src/**/*"],"compilerOptions":{"allowJs":true}}');
        write("src/helper.ts", "export const value = 1;");
        write("src/helper.extra.ts", "export const extra = 3;");
        write("src/actual.js", "export default 2;");
        const source = [
            'import { value } from "./helper.js";',
            'import actual from "./actual.js";',
            'import missing from "./missing.js";',
            'import { extra } from "./helper.extra.js";',
            'const text = "another.js";',
            'const notAnArray = { push(x: number): number { return x; } };',
            'notAnArray.push(value);',
            'const items: number[] = []; items.push(actual);',
            'const lf = "\\n";',
            'const backslash = "\\\\n";',
        ].join("\r\n");
        write("src/index.ts", source);
        const report = JSON.parse(run("tsconfig.json"));
        equal(report.legacyProfile, "Legacy");
        deepStrictEqual(report.files, ["src/actual.js", "src/helper.extra.ts", "src/helper.ts", "src/index.ts"]);
        const imports = report.findings.filter((finding: { code: string }) => finding.code === "COPE-MIGRATE-0101");
        equal(imports.length, 2);
        const fix = imports[0].edit;
        equal(fix.scope, "copelandCopy");
        equal(source.slice(fix.start, fix.start + fix.length), "./helper.js");
        equal(fix.replacement, "./helper");
        equal(imports[0].line, 1);
        equal(imports[1].edit.replacement, "./helper.extra.ts");
        equal(report.findings.filter((finding: { code: string }) => finding.code === "COPE-MIGRATE-0107").length, 1);
        equal(report.findings.filter((finding: { code: string }) => finding.code === "COPE-MIGRATE-0108").length, 1);
        equal(fs.readFileSync(path.join(root, "src/index.ts"), "utf8"), source);
        equal(run("tsconfig.json"), JSON.stringify(report));
    });

    test("preflights generated paths with TypeScript globs in referenced projects", () => {
        write("tsconfig.json", '{"files":[],"references":[{"path":"./library"}]}');
        write("library/tsconfig.json", '{"include":["**/*"],"exclude":["skip"],"compilerOptions":{"composite":true,"allowJs":true}}');
        write("library/index.ts", "export const value = 1;");
        const overlapping = path.join(root, "library/copeland/src/Main.ts");
        const generated = path.join(root, "library/copeland/dist/main.js");
        const excluded = path.join(root, "library/skip/Main.ts");
        const sibling = path.join(root, "other-copeland/Main.ts");
        const report = JSON.parse(run("tsconfig.json", [overlapping, generated, excluded, sibling]));
        deepStrictEqual(report.includedFiles, [overlapping, generated]);
        ok(!fs.existsSync(path.join(root, "library/copeland")));
    });
});
