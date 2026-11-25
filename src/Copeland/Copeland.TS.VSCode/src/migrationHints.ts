import { execFile } from "child_process";
import * as path from "path";
import * as vscode from "vscode";
import { resolveTscl } from "./toolchain";

interface MigrationReport {
    schemaVersion: number;
    project: string;
    findings: Array<{
        file: string;
        code: string;
        message: string;
        line: number;
        column: number;
        endLine: number;
        endColumn: number;
    }>;
}

export function registerMigrationHints(context: vscode.ExtensionContext, output: vscode.OutputChannel): void {
    const diagnostics = vscode.languages.createDiagnosticCollection("copeland-migration");
    let latestRun = 0;
    context.subscriptions.push(
        diagnostics,
        vscode.workspace.onDidChangeTextDocument(event => diagnostics.delete(event.document.uri)),
        vscode.commands.registerCommand("copeland.clearMigrationHints", () => {
            latestRun++;
            diagnostics.clear();
        }),
        vscode.commands.registerCommand("copeland.showMigrationHints", async () => {
            const active = vscode.window.activeTextEditor?.document.uri;
            const folder = (active && vscode.workspace.getWorkspaceFolder(active)) ?? vscode.workspace.workspaceFolders?.[0];
            if (!folder || folder.uri.scheme !== "file" || !vscode.workspace.isTrusted) {
                await vscode.window.showInformationMessage("Migration hints require a trusted local TypeScript workspace.");
                return;
            }

            const run = ++latestRun;
            diagnostics.clear();
            const versions = new Map(vscode.workspace.textDocuments.map(document => [document.uri.toString(), document.version]));
            try {
                const config = await findConfig(folder, active);
                const report = await queryReport(folder.uri.fsPath, config);
                if (run !== latestRun) {
                    return;
                }
                const byFile = new Map<string, { uri: vscode.Uri; hints: vscode.Diagnostic[] }>();
                for (const finding of report.findings) {
                    const uri = vscode.Uri.file(path.resolve(path.dirname(report.project), finding.file));
                    if (!vscode.workspace.getWorkspaceFolder(uri)) {
                        continue;
                    }
                    const key = uri.toString();
                    let bucket = byFile.get(key);
                    if (!bucket) {
                        bucket = { uri, hints: [] };
                        byFile.set(key, bucket);
                    }
                    const range = new vscode.Range(finding.line - 1, finding.column - 1, finding.endLine - 1, finding.endColumn - 1);
                    const diagnostic = new vscode.Diagnostic(range, finding.message, vscode.DiagnosticSeverity.Warning);
                    diagnostic.code = finding.code;
                    diagnostic.source = "Copeland migration";
                    bucket.hints.push(diagnostic);
                }

                const openDocuments = new Map(vscode.workspace.textDocuments.map(document => [document.uri.toString(), document]));
                for (const [key, { uri, hints }] of byFile) {
                    const document = openDocuments.get(key);
                    if (!document || (!document.isDirty && (!versions.has(key) || versions.get(key) === document.version))) {
                        diagnostics.set(uri, hints);
                    }
                }
                output.appendLine("[migration] " + report.findings.length + " findings for " + report.project);
            } catch (error) {
                if (run === latestRun) {
                    const message = error instanceof Error ? error.message : String(error);
                    output.appendLine("[migration] " + message);
                    await vscode.window.showErrorMessage(message);
                }
            }
        })
    );
}

async function findConfig(folder: vscode.WorkspaceFolder, active: vscode.Uri | undefined): Promise<string | undefined> {
    if (active && vscode.workspace.getWorkspaceFolder(active)?.uri.toString() === folder.uri.toString()) {
        if (/^tsconfig(?:[.][^/\\]+)?[.]json$/.test(path.basename(active.fsPath))) {
            return active.fsPath;
        }
        for (let directory = path.dirname(active.fsPath); directory !== folder.uri.fsPath; directory = path.dirname(directory)) {
            const candidate = vscode.Uri.file(path.join(directory, "tsconfig.json"));
            if (await isFile(candidate)) {
                return candidate.fsPath;
            }
            if (path.dirname(directory) === directory) {
                break;
            }
        }
    }
    return undefined;
}

async function isFile(uri: vscode.Uri): Promise<boolean> {
    try {
        return Boolean((await vscode.workspace.fs.stat(uri)).type & vscode.FileType.File);
    } catch {
        return false;
    }
}

async function queryReport(cwd: string, config: string | undefined): Promise<MigrationReport> {
    const tool = await resolveTscl(cwd);
    const args = [...tool.arguments, "migrate", "report", "--format", "json"];
    if (config) {
        args.push("-p", config);
    }
    return new Promise((resolve, reject) => {
        execFile(tool.command, args, { cwd, windowsHide: true, timeout: 60000, maxBuffer: 32 * 1024 * 1024 }, (error, stdout, stderr) => {
            if (error) {
                reject(new Error(stderr.trim() || error.message));
                return;
            }
            try {
                const report = JSON.parse(stdout) as MigrationReport;
                if (report.schemaVersion !== 1 || typeof report.project !== "string" || !Array.isArray(report.findings)) {
                    throw new Error("Invalid tscl migration report.");
                }
                resolve(report);
            } catch (parseError) {
                reject(parseError);
            }
        });
    });
}
