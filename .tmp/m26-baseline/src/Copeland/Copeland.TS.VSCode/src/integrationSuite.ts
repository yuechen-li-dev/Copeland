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
        const domainUri = vscode.Uri.file(process.env.COPLAND_VSCODE_TEST_TSCL_FILE ?? path.join(root, "src/copeland/Domain.ts"));
        const legacyUri = vscode.Uri.file(process.env.COPLAND_VSCODE_TEST_TSC_FILE ?? path.join(root, "src/legacy/Legacy.ts"));
        const ownershipUri = vscode.Uri.file(path.join(root, "obj/copeland/workspace/editor-ownership.generated.json"));
        let originalOwnership = "";

        suiteSetup(async () => {
            assert.ok(workspace, "The integration runner must open the mixed workspace fixture.");
            assert.ok(cli, "The integration runner must provide a published local tscl toolchain.");
            await vscode.workspace.getConfiguration("copeland", workspace!.uri).update("tsclPath", cli, vscode.ConfigurationTarget.Workspace);
            await vscode.workspace.getConfiguration("copeland", workspace!.uri).update("languageServer.trace", "verbose", vscode.ConfigurationTarget.Workspace);
            const extension = vscode.extensions.getExtension("copeland.copeland-ts");
            assert.ok(extension, "Copeland TS extension must be available to the extension host.");
            await extension!.activate();
            await waitFor(async () => {
                try {
                    originalOwnership = await fs.readFile(ownershipUri.fsPath, "utf8");
                    return true;
                } catch {
                    return false;
                }
            });
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
            try {
                await waitFor(() => vscode.languages.getDiagnostics(domainUri).length === 0);
            } catch {
                throw new Error(`Expected diagnostics to clear; received ${JSON.stringify(vscode.languages.getDiagnostics(domainUri))}.`);
            }
            assert.equal(activeDocument(domainUri)?.languageId, "copeland-typescript");
            assert.deepEqual(vscode.languages.getDiagnostics(domainUri), []);
        });

        test("keeps a tsc-owned file on the built-in TypeScript language", async () => {
            const legacy = await vscode.workspace.openTextDocument(legacyUri);
            await vscode.window.showTextDocument(legacy);
            await waitFor(() => activeDocument(legacyUri)?.languageId === "typescript");
            assert.equal(activeDocument(legacyUri)?.languageId, "typescript");
            assert.ok(vscode.languages.getDiagnostics(legacyUri).every((diagnostic) => diagnostic.source !== "tscl"));
        });

        test("provides hover, completion, and authored-source navigation", async () => {
            const document = await vscode.workspace.openTextDocument(domainUri);
            await vscode.window.showTextDocument(document);
            await waitFor(() => activeDocument(domainUri)?.languageId === "copeland-typescript");
            assert.equal(activeDocument(domainUri)?.languageId, "copeland-typescript");
            await new Promise((resolve) => setTimeout(resolve, 250));
            const completions = await vscode.commands.executeCommand<vscode.CompletionList>(
                "vscode.executeCompletionItemProvider",
                domainUri,
                new vscode.Position(0, 0));
            assert.ok(completions.items.some((item) => item.label === "function"));

            let hoverFound = false;
            for (const candidate of ["NpmError", "String", "Helper", "camelCase"]) {
                const candidateOffset = document.getText().lastIndexOf(candidate);
                const candidatePosition = document.positionAt(candidateOffset + 1);
                const hovers = await vscode.commands.executeCommand<vscode.Hover[]>(
                    "vscode.executeHoverProvider",
                    domainUri,
                    candidatePosition);
                hoverFound ||= Boolean(hovers && hovers.length > 0);
            }
            assert.ok(hoverFound);

            const helperOffset = document.getText().lastIndexOf("Helper");
            const helperHovers = await vscode.commands.executeCommand<vscode.Hover[]>(
                "vscode.executeHoverProvider",
                domainUri,
                document.positionAt(helperOffset + 1));
            const helperHoverText = helperHovers
                ?.flatMap((hover) => hover.contents)
                .map((content) => typeof content === "string" ? content : content.value)
                .join("\n") ?? "";
            assert.match(helperHoverText, /CopelandHello\.Helper/);

            const importedOffset = document.getText().lastIndexOf("normalizeName");
            assert.ok(importedOffset >= 0);
            const importedPosition = document.positionAt(importedOffset + 1);
            const definitions = await vscode.commands.executeCommand<Array<vscode.Location | vscode.LocationLink>>(
                "vscode.executeDefinitionProvider",
                domainUri,
                importedPosition);
            assert.ok(definitions && definitions.length > 0);

            const npmOffset = document.getText().lastIndexOf("camelCase");
            const npmDefinitions = await vscode.commands.executeCommand<Array<vscode.Location | vscode.LocationLink>>(
                "vscode.executeDefinitionProvider",
                domainUri,
                document.positionAt(npmOffset + 1));
            assert.ok(npmDefinitions && npmDefinitions.length > 0);
            const npmTarget = npmDefinitions[0] instanceof vscode.Location
                ? npmDefinitions[0].uri
                : npmDefinitions[0].targetUri;
            assert.equal(path.basename(npmTarget.fsPath), "lodash-es.json");
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
                assert.ok(vscode.languages.getDiagnostics(domainUri).every((diagnostic) => diagnostic.range.start.line >= 0));
            } finally {
                await replaceDocument(activeDocument(domainUri)!, validText);
            }

            try {
                await waitFor(() => vscode.languages.getDiagnostics(domainUri).length === 0);
            } catch {
                throw new Error(`Expected repaired diagnostics to clear; received ${JSON.stringify(vscode.languages.getDiagnostics(domainUri))}.`);
            }
        });

        test("switches an open buffer when generated ownership transfers", async () => {
            const legacy = await vscode.workspace.openTextDocument(legacyUri);
            const transferred = JSON.parse(originalOwnership) as { files: Array<{ path: string; owner: string }> };
            const relativeLegacyPath = path.relative(root, legacyUri.fsPath).replaceAll("\\", "/");
            const entry = transferred.files.find((file) => file.path === relativeLegacyPath);
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

async function waitFor(predicate: () => boolean | Promise<boolean>, timeout = 10000): Promise<void> {
    const started = Date.now();
    while (!await predicate()) {
        if (Date.now() - started > timeout) {
            throw new Error("Timed out waiting for VS Code language routing state.");
        }

        await new Promise((resolve) => setTimeout(resolve, 50));
    }
}
