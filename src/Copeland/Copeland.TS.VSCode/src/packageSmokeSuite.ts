import { strict as assert } from "assert";
import * as fs from "fs/promises";
import * as vscode from "vscode";

export async function run(): Promise<void> {
    const mocha = new (await import("mocha")).default({ ui: "tdd", timeout: 30000 });
    mocha.suite.emit("pre-require", globalThis, "packageSmokeSuite", mocha);

    suite("Copeland packaged VSIX", () => {
        if (process.env.COPELAND_INTEROP_EDITOR === "1") {
            test("routes mixed sources and refreshes NuGet bindings in an installed TSPack project", async () => {
                const folder = vscode.workspace.workspaceFolders![0].uri;
                const installed = vscode.extensions.getExtension("copeland.copeland-ts");
                const eslint = vscode.extensions.getExtension("dbaeumer.vscode-eslint");
                assert.ok(installed);
                assert.ok(eslint);
                assert.equal(eslint.packageJSON.version, "3.0.24");
                await installed.activate();
                await eslint.activate();

                const legacy = vscode.Uri.joinPath(folder, "legacy/Legacy.ts");
                await show(legacy);
                await waitFor(() => {
                    const diagnostics = vscode.languages.getDiagnostics(legacy);
                    return activeDocument(legacy)?.languageId === "typescript"
                        && diagnostics.some(item => item.source === "ts" && item.code === 2339)
                        && diagnostics.some(item => item.source === "eslint" && item.severity === vscode.DiagnosticSeverity.Warning);
                }, 20000, legacy);

                const manifest = vscode.Uri.joinPath(folder, "manifest.tsx");
                await show(manifest);
                await waitFor(() => activeDocument(manifest)?.languageId === "copeland-typescriptreact"
                    && vscode.languages.getDiagnostics(manifest).length === 0, 15000, manifest);

                const entry = vscode.Uri.joinPath(folder, "src/Main.ts");
                await show(entry);
                const bindingCode = "CTS-LSP-NUGET-BINDINGS";
                await waitFor(() => {
                    const diagnostics = vscode.languages.getDiagnostics(entry);
                    return activeDocument(entry)?.languageId === "copeland-typescript"
                        && diagnostics.length === 1 && diagnostics[0].code === bindingCode
                        && diagnostics[0].severity === vscode.DiagnosticSeverity.Information;
                }, 15000, entry);
                const diagnostic = vscode.languages.getDiagnostics(entry)[0];
                const actions = await vscode.commands.executeCommand<vscode.CodeAction[]>(
                    "vscode.executeCodeActionProvider", entry, diagnostic.range, vscode.CodeActionKind.QuickFix.value);
                const action = actions.find(item => item.command?.command === "copeland.generateNugetBindings");
                assert.ok(action?.command, "Missing NuGet binding quick fix.");
                await vscode.commands.executeCommand(action.command.command, ...(action.command.arguments ?? []));
                await waitFor(() => vscode.languages.getDiagnostics(entry).length === 0, 15000, entry);

                const generated = vscode.Uri.joinPath(folder, "src/generated/Nuget.ts");
                await show(generated);
                await waitFor(() => activeDocument(generated)?.languageId === "copeland-typescript"
                    && vscode.languages.getDiagnostics(generated).length === 0, 15000, generated);

                const newSource = vscode.Uri.joinPath(folder, "src/NewEditorProbe.tsx");
                await fs.writeFile(newSource.fsPath, 'export record Probe { name: string; }\nexport function Bad(): Probe { return { name: 42 }; }\n');
                await show(newSource);
                await waitFor(() => activeDocument(newSource)?.languageId === "copeland-typescriptreact"
                    && vscode.languages.getDiagnostics(newSource).some(item => item.source === "tscl"), 15000, newSource);
                assert.ok(vscode.languages.getDiagnostics(newSource).every(item => item.source === "tscl"));
            }).timeout(120000);
            return;
        }

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

async function show(uri: vscode.Uri): Promise<void> {
    const document = await vscode.workspace.openTextDocument(uri);
    await vscode.window.showTextDocument(document);
}

function activeDocument(uri: vscode.Uri): vscode.TextDocument | undefined {
    return vscode.workspace.textDocuments.find(
        (document) => document.uri.toString() === uri.toString()
    );
}

async function waitFor(predicate: () => boolean, timeout = 15000, uri?: vscode.Uri): Promise<void> {
    const started = Date.now();
    while (!predicate()) {
        if (Date.now() - started > timeout) {
            const details = uri ? {
                path: uri.fsPath,
                language: activeDocument(uri)?.languageId,
                diagnostics: vscode.languages.getDiagnostics(uri).slice(0, 8)
            } : {};
            throw new Error("Timed out waiting for editor state. " + JSON.stringify(details));
        }

        await new Promise((resolve) => setTimeout(resolve, 50));
    }
}
