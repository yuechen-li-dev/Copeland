import * as path from "path";
import { execFileSync } from "child_process";
import { mkdir, readFile, unlink, writeFile } from "fs/promises";
import { downloadAndUnzipVSCode, runTests, runVSCodeCommand } from "@vscode/test-electron";

async function main(): Promise<void> {
    if (process.env.COPELAND_INTEROP_RECEIPT) {
        await runInteropEditor();
        return;
    }
    const testHarnessPath = path.resolve(__dirname, "..", "test-harness");
    const workspace = requiredPath("COPELAND_VSCODE_TEST_WORKSPACE");
    const toolchain = requiredPath("COPELAND_VSCODE_TEST_TSCL_PATH");
    const userDataDirectory = requiredPath("COPELAND_VSCODE_TEST_USER_DATA");
    const extensionsDirectory = requiredPath("COPELAND_VSCODE_TEST_EXTENSIONS");
    const extensionTestsPath = path.resolve(__dirname, "packageSmokeSuite.js");

    await runTests({
        // Only the inert test harness is loaded as a development extension.
        // Copeland itself must come from the isolated installed VSIX directory.
        extensionDevelopmentPath: testHarnessPath,
        extensionTestsPath,
        extensionTestsEnv: {
            COPELAND_VSCODE_TEST_TSCL_PATH: toolchain,
            COPELAND_VSCODE_TEST_TSX_FILE: path.join(workspace, "src", "GreetingDocument.tsx")
        },
        launchArgs: [
            workspace,
            "--disable-workspace-trust",
            "--user-data-dir", userDataDirectory,
            "--extensions-dir", extensionsDirectory
        ]
    });
}

async function runInteropEditor(): Promise<void> {
    const receipt = JSON.parse(await readFile(requiredPath("COPELAND_INTEROP_RECEIPT"), "utf8")) as {
        roundtrip: string;
        tscl: string;
        nugetPackages: string;
    };
    const workspace = receipt.roundtrip;
    const root = path.dirname(workspace);
    const userData = path.join(root, "interop-vscode-profile");
    const extensions = path.join(root, "interop-vscode-extensions");
    const version = "1.142.0";
    const profile = ["--user-data-dir", userData, "--extensions-dir", extensions];
    const settings = path.join(workspace, ".vscode");
    const userSettings = path.join(userData, "User");
    const legacy = path.join(workspace, "legacy");
    await mkdir(settings, { recursive: true });
    await mkdir(userSettings, { recursive: true });
    await mkdir(legacy, { recursive: true });
    await writeFile(path.join(userSettings, "settings.json"), JSON.stringify({
        "extensions.autoUpdate": false,
        "extensions.autoCheckUpdates": false
    }));
    await writeFile(path.join(settings, "settings.json"), JSON.stringify({ "copeland.tsclPath": receipt.tscl }));
    await writeFile(path.join(legacy, "Legacy.ts"), "export const probe = 1;\nprobe.missingField;\ndebugger;\n");
    await writeFile(path.join(workspace, "tsconfig.json"), JSON.stringify({ include: ["legacy/**/*.ts"] }));
    const tsconfig = path.join(workspace, "tsconfig.tsx");
    const sampleConfig = await readFile(tsconfig, "utf8");
    await writeFile(tsconfig, sampleConfig
        .replace("    tscl: {", '    tsc: { include: ["legacy/**"], compilerOptions: { strict: true } },\n    tscl: {'));
    // Leave the original two TSPack descriptors in place to test an open
    // editor whose generated source membership changes before a rebuild.
    await unlink(path.join(workspace, "src", "generated", "Nuget.ts"));
    await writeFile(path.join(workspace, "eslint.config.mjs"),
        'export default [{ files: ["{src,legacy}/**/*.ts"], rules: { "no-debugger": "warn" } }];\n');
    if (process.platform === "win32") {
        const npm = path.join(path.dirname(process.execPath), "node_modules", "npm", "bin", "npm-cli.js");
        execFileSync(process.execPath, [npm, "install", "--no-save", "--ignore-scripts", "eslint@9.39.1"], { cwd: workspace, stdio: "inherit" });
    } else {
        execFileSync("npm", ["install", "--no-save", "--ignore-scripts", "eslint@9.39.1"], { cwd: workspace, stdio: "inherit" });
    }

    const installed = await runVSCodeCommand([...profile, "--install-extension", requiredPath("COPELAND_INTEROP_VSIX"), "--force"], { version });
    console.log(installed.stdout.trim());
    const eslint = await runVSCodeCommand([...profile, "--install-extension", "dbaeumer.vscode-eslint@3.0.24", "--force"], { version });
    console.log(eslint.stdout.trim());
    await runTests({
        vscodeExecutablePath: await downloadAndUnzipVSCode(version),
        extensionDevelopmentPath: path.resolve(__dirname, "..", "test-harness"),
        extensionTestsPath: path.resolve(__dirname, "packageSmokeSuite.js"),
        extensionTestsEnv: {
            COPELAND_INTEROP_EDITOR: "1",
            DOTNET_CLI_HOME: path.join(root, "dotnet-home"),
            NUGET_PACKAGES: receipt.nugetPackages,
        },
        launchArgs: [workspace, ...profile]
    });
}

function requiredPath(name: string): string {
    const value = process.env[name]?.trim();
    if (!value) {
        throw new Error(`${name} must name an isolated installed-artifact path.`);
    }

    return path.resolve(value);
}

void main().catch((error) => {
    console.error(error);
    process.exitCode = 1;
});
