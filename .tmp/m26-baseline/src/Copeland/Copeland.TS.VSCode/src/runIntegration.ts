import * as path from "path";
import { execFile } from "child_process";
import { runTests } from "@vscode/test-electron";

async function main(): Promise<void> {
    const extensionDevelopmentPath = path.resolve(__dirname, "..");
    const extensionTestsPath = path.resolve(__dirname, "integrationSuite.js");
    const workspace = path.resolve(__dirname, "../../../../samples/copeland-ts/workspace-m0");
    const repositoryRoot = path.resolve(__dirname, "../../../..");
    const testToolchain = path.resolve(extensionDevelopmentPath, ".vscode-test/copeland-toolchain");
    const userDataDirectory = path.resolve(extensionDevelopmentPath, `.vscode-test/user-data-${Date.now()}`);
    await publishTestToolchain(repositoryRoot, testToolchain);
    process.env.COPLAND_VSCODE_TEST_TSCL_PATH = path.join(testToolchain, "Copeland.Cli.exe");
    await runTests({
        extensionDevelopmentPath,
        extensionTestsPath,
        extensionTestsEnv: {
            COPELAND_VSCODE_TEST_TSCL_PATH: process.env.COPLAND_VSCODE_TEST_TSCL_PATH
        },
        launchArgs: [workspace, "--disable-workspace-trust", "--user-data-dir", userDataDirectory]
    });
}

async function publishTestToolchain(repositoryRoot: string, outputDirectory: string): Promise<void> {
    const project = path.join(repositoryRoot, "src/Copeland/Copeland.Cli/Copeland.Cli.csproj");
    await new Promise<void>((resolve, reject) => {
        execFile("dotnet", ["publish", project, "--no-restore", "--output", outputDirectory], { cwd: repositoryRoot, windowsHide: true }, (error, _stdout, stderr) => {
            if (error) {
                reject(new Error(stderr.trim() || error.message));
                return;
            }

            resolve();
        });
    });
}

void main().catch((error) => {
    console.error(error);
    process.exitCode = 1;
});
