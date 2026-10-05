import { strict as assert } from "assert";
import * as vscode from "vscode";

export async function run(): Promise<void> {
    const mocha = new (await import("mocha")).default({ ui: "tdd", timeout: 30000 });
    mocha.suite.emit("pre-require", globalThis, "packageSmokeSuite", mocha);

    suite("Copeland packaged VSIX", () => {
        test("activates from the installed extension and owns generated TSX", async () => {
            const extension = vscode.extensions.getExtension("copeland.copeland-ts");
            assert.ok(extension, "The installed Copeland TS extension must be visible.");
            assert.equal(extension!.packageJSON.version, "0.1.0-preview.1");

            const workspace = vscode.workspace.workspaceFolders?.[0];
            assert.ok(workspace, "The generated bootstrap workspace must be open.");
            const toolchain = process.env.COPELAND_VSCODE_TEST_TSCL_PATH;
            assert.ok(toolchain, "The package-only tscl path must be provided.");
            await vscode.workspace.getConfiguration("copeland", workspace!.uri).update(
                "tsclPath",
                toolchain,
                vscode.ConfigurationTarget.Workspace
            );

            await extension!.activate();
            assert.ok(extension!.isActive, "The installed extension did not activate.");

            // workspaceContains may activate the installed extension before the
            // isolated tool override is written. Synchronize once explicitly
            // through the installed extension's public command surface.
            await vscode.commands.executeCommand("copeland.workspaceSync", workspace!.uri);

            const sourcePath = process.env.COPELAND_VSCODE_TEST_TSX_FILE;
            assert.ok(sourcePath, "The generated TSX file path must be provided.");
            const source = await vscode.workspace.openTextDocument(vscode.Uri.file(sourcePath!));
            await vscode.window.showTextDocument(source);
            await waitFor(() => activeDocument(source.uri)?.languageId === "copeland-typescriptreact");
            assert.equal(activeDocument(source.uri)?.languageId, "copeland-typescriptreact");

            await vscode.commands.executeCommand("copeland.showLanguageServerOutput");
        });
    });

    return new Promise<void>((resolve, reject) => {
        mocha.run((failures) => {
            if (failures) {
                reject(new Error(`${failures} package smoke test(s) failed.`));
                return;
            }

            resolve();
        });
    });
}

function activeDocument(uri: vscode.Uri): vscode.TextDocument | undefined {
    return vscode.workspace.textDocuments.find(
        (document) => document.uri.toString() === uri.toString()
    );
}

async function waitFor(predicate: () => boolean, timeout = 15000): Promise<void> {
    const started = Date.now();
    while (!predicate()) {
        if (Date.now() - started > timeout) {
            throw new Error("Timed out waiting for Copeland TSX ownership.");
        }

        await new Promise((resolve) => setTimeout(resolve, 50));
    }
}
