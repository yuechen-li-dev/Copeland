import * as path from "path";
import { runTests } from "@vscode/test-electron";

async function main(): Promise<void> {
    const extensionDevelopmentPath = requiredPath("COPELAND_VSCODE_INSTALLED_EXTENSION_PATH");
    const workspace = requiredPath("COPELAND_VSCODE_TEST_WORKSPACE");
    const toolchain = requiredPath("COPELAND_VSCODE_TEST_TSCL_PATH");
    const userDataDirectory = requiredPath("COPELAND_VSCODE_TEST_USER_DATA");
    const extensionsDirectory = requiredPath("COPELAND_VSCODE_TEST_EXTENSIONS");
    const extensionTestsPath = path.resolve(__dirname, "integrationSuite.js");
    process.env.COPLAND_VSCODE_TEST_TSCL_PATH = toolchain;
    process.env.COPLAND_VSCODE_TEST_TSCL_FILE = path.join(workspace, "src", "copeland", "Program.ts");
    process.env.COPLAND_VSCODE_TEST_TSC_FILE = path.join(workspace, "src", "traditional", "Traditional.ts");

    await runTests({
        extensionDevelopmentPath,
        extensionTestsPath,
        extensionTestsEnv: {
            COPELAND_VSCODE_TEST_TSCL_PATH: toolchain,
            COPELAND_VSCODE_TEST_TSCL_FILE: path.join(workspace, "src", "copeland", "Program.ts"),
            COPELAND_VSCODE_TEST_TSC_FILE: path.join(workspace, "src", "traditional", "Traditional.ts")
        },
        launchArgs: [
            workspace,
            "--disable-workspace-trust",
            "--user-data-dir", userDataDirectory,
            "--extensions-dir", extensionsDirectory
        ]
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
