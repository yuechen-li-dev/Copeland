import * as path from "path";
import { runTests } from "@vscode/test-electron";

async function main(): Promise<void> {
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
