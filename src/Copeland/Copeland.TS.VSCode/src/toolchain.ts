import { ChildProcess, execFile, spawn } from "child_process";
import * as path from "path";
import * as vscode from "vscode";

export interface CommandResult {
    exitCode: number;
    output: string;
}

export interface ToolchainVersion {
    extension: string;
    server: string;
    project?: string;
}

export function tsclCommand(): string {
    const configured = vscode.workspace.getConfiguration("copeland").get<string>("tsclPath", "").trim();
    if (configured) {
        return configured;
    }

    return process.env.COPLAND_VSCODE_TEST_TSCL_PATH?.trim() || "tscl";
}

export async function runTool(command: string, args: string[], cwd: string, output: vscode.OutputChannel): Promise<CommandResult> {
    output.appendLine(`> ${command} ${args.join(" ")}`);
    return new Promise<CommandResult>((resolve) => {
        const child = spawn(command, args, { cwd, shell: false, windowsHide: true });
        child.stdout.on("data", (data: Buffer) => output.append(data.toString()));
        child.stderr.on("data", (data: Buffer) => output.append(data.toString()));
        child.on("error", (error) => {
            output.appendLine(error.message);
            resolve({ exitCode: -1, output: error.message });
        });
        child.on("close", (exitCode) => resolve({ exitCode: exitCode ?? -1, output: "" }));
    });
}

export async function queryServerVersion(command: string, cwd: string): Promise<string> {
    return new Promise<string>((resolve, reject) => {
        execFile(command, ["language-server", "--version"], { cwd, windowsHide: true }, (error, stdout, stderr) => {
            if (error) {
                reject(new Error(stderr.trim() || error.message));
                return;
            }

            resolve(stdout.trim());
        });
    });
}

export function isCompatibleVersion(expected: ToolchainVersion): boolean {
    return [expected.server, expected.project].filter((version): version is string => Boolean(version)).every((version) => sameMajorMinor(expected.extension, version));
}

function sameMajorMinor(left: string, right: string): boolean {
    const leftParts = left.split(".");
    const rightParts = right.split(".");
    return leftParts.length >= 2 && rightParts.length >= 2 && leftParts[0] === rightParts[0] && leftParts[1] === rightParts[1];
}

export function spawnLanguageServer(command: string, cwd: string, output: vscode.OutputChannel): ChildProcess {
    const child = spawn(command, ["language-server"], { cwd, shell: false, windowsHide: true });
    child.stderr.on("data", (data: Buffer) => output.append(data.toString()));
    child.on("error", (error) => output.appendLine(`[launch] ${error.message}`));
    return child;
}

export async function readProjectVersion(projectPath: string | undefined): Promise<string | undefined> {
    if (!projectPath) {
        return undefined;
    }

    try {
        const bytes = await vscode.workspace.fs.readFile(vscode.Uri.file(projectPath));
        const project = Buffer.from(bytes).toString("utf8");
        const packageReference = /<PackageReference\s+[^>]*(?:Include|Update)="Copeland(?:\.TS)?"[^>]*Version="([^"]+)"/i.exec(project);
        const property = /<Copeland(?:Ts)?Version>\s*([^<\s]+)\s*<\/Copeland(?:Ts)?Version>/i.exec(project);
        return packageReference?.[1] ?? property?.[1];
    } catch {
        return undefined;
    }
}

export function projectPath(workspaceRoot: string, project: string | undefined): string | undefined {
    if (!project) {
        return undefined;
    }

    return path.resolve(workspaceRoot, project);
}
