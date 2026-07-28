import { strict as assert } from "assert";
import * as fs from "fs/promises";
import * as path from "path";
import * as vscode from "vscode";

export async function run(): Promise<void> {
    const mocha = new (await import("mocha")).default({ ui: "tdd", timeout: 30000 });
    mocha.suite.emit("pre-require", globalThis, "integrationSuite", mocha);

    suite("Copeland TS mixed workspace", () => {
        const workspace = vscode.workspace.workspaceFolders?.[0];
        const root = workspace?.uri.fsPath ?? "";
        const cli = process.env.COPLAND_VSCODE_TEST_TSCL_PATH ?? "";
        const domainUri = vscode.Uri.file(path.join(root, "src/copeland/Domain.ts"));
        const legacyUri = vscode.Uri.file(path.join(root, "src/legacy/Legacy.ts"));
        const ownershipUri = vscode.Uri.file(path.join(root, "obj/copeland/workspace/editor-ownership.generated.json"));
        let originalOwnership = "";

        suiteSetup(async () => {
            assert.ok(workspace, "The integration runner must open the mixed workspace fixture.");
            assert.ok(cli, "The integration runner must provide a published local tscl toolchain.");
            originalOwnership = await fs.readFile(ownershipUri.fsPath, "utf8");
            await vscode.workspace.getConfiguration("copeland", workspace!.uri).update("tsclPath", cli, vscode.ConfigurationTarget.Workspace);
            await vscode.workspace.getConfiguration("copeland", workspace!.uri).update("languageServer.trace", "verbose", vscode.ConfigurationTarget.Workspace);
            const extension = vscode.extensions.getExtension("copeland.copeland-ts");
            assert.ok(extension, "Copeland TS extension must be available to the extension host.");
            await extension!.activate();
            await vscode.commands.executeCommand("copeland.reloadWorkspaceOwnership", workspace!.uri);
        });

        suiteTeardown(async () => {
            if (originalOwnership) {
                await fs.writeFile(ownershipUri.fsPath, originalOwnership, "utf8");
            }
        });

        test("routes the generated tscl file to Copeland and keeps false TypeScript squiggles out", async () => {
            const document = await vscode.workspace.openTextDocument(domainUri);
            await vscode.window.showTextDocument(document);
            await waitFor(() => activeDocument(domainUri)?.languageId === "copeland-typescript");
            await waitFor(() => vscode.languages.getDiagnostics(domainUri).length === 0);
            assert.equal(activeDocument(domainUri)?.languageId, "copeland-typescript");
            assert.deepEqual(vscode.languages.getDiagnostics(domainUri), []);
        });

        test("keeps a tsc-owned file on the built-in TypeScript language", async () => {
            const legacy = await vscode.workspace.openTextDocument(legacyUri);
            await vscode.window.showTextDocument(legacy);
            await waitFor(() => activeDocument(legacyUri)?.languageId === "typescript");
            await waitFor(() => vscode.languages.getDiagnostics(legacyUri).length > 0);
            assert.equal(activeDocument(legacyUri)?.languageId, "typescript");
            assert.ok(vscode.languages.getDiagnostics(legacyUri).every((diagnostic) => diagnostic.source !== "tscl"));
        });

        test("publishes a Copeland error for an unsaved invalid buffer and clears it after repair", async () => {
            const document = await vscode.workspace.openTextDocument(domainUri);
            await vscode.window.showTextDocument(document);
            const validText = document.getText();
            try {
                await replaceDocument(document, "function Main(");
                try {
                    await waitFor(() => vscode.languages.getDiagnostics(domainUri).some((diagnostic) => diagnostic.source === "tscl"));
                } catch {
                    throw new Error(`Expected a tscl diagnostic; received ${JSON.stringify(vscode.languages.getDiagnostics(domainUri))}.`);
                }
                assert.ok(vscode.languages.getDiagnostics(domainUri).some((diagnostic) => diagnostic.range.start.line === 0));
            } finally {
                await replaceDocument(activeDocument(domainUri)!, validText);
            }

            await waitFor(() => vscode.languages.getDiagnostics(domainUri).length === 0);
        });

        test("switches an open buffer when generated ownership transfers", async () => {
            const legacy = await vscode.workspace.openTextDocument(legacyUri);
            const transferred = JSON.parse(originalOwnership) as { files: Array<{ path: string; owner: string }> };
            const entry = transferred.files.find((file) => file.path === "src/legacy/Legacy.ts");
            assert.ok(entry);
            entry!.owner = "tscl";
            await fs.writeFile(ownershipUri.fsPath, JSON.stringify(transferred, null, 2), "utf8");
            await vscode.commands.executeCommand("copeland.reloadWorkspaceOwnership", workspace!.uri);
            await waitFor(() => activeDocument(legacyUri)?.languageId === "copeland-typescript");
            await fs.writeFile(ownershipUri.fsPath, originalOwnership, "utf8");
            await vscode.commands.executeCommand("copeland.reloadWorkspaceOwnership", workspace!.uri);
            await waitFor(() => activeDocument(legacyUri)?.languageId === "typescript");
            assert.equal(legacy.uri.toString(), legacyUri.toString());
        });
    });

    return new Promise<void>((resolve, reject) => mocha.run((failures) => failures ? reject(new Error(`${failures} integration test(s) failed.`)) : resolve()));
}

function activeDocument(uri: vscode.Uri): vscode.TextDocument | undefined {
    return vscode.workspace.textDocuments.find((document) => document.uri.toString() === uri.toString());
}

async function replaceDocument(document: vscode.TextDocument, text: string): Promise<void> {
    const edit = new vscode.WorkspaceEdit();
    edit.replace(document.uri, new vscode.Range(document.positionAt(0), document.positionAt(document.getText().length)), text);
    assert.ok(await vscode.workspace.applyEdit(edit));
}

async function waitFor(predicate: () => boolean, timeout = 10000): Promise<void> {
    const started = Date.now();
    while (!predicate()) {
        if (Date.now() - started > timeout) {
            throw new Error("Timed out waiting for VS Code language routing state.");
        }

        await new Promise((resolve) => setTimeout(resolve, 50));
    }
}
