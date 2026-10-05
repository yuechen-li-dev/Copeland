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

export interface ToolInvocation {
    command: string;
    arguments: string[];
    source: string;
}

export async function resolveTscl(workspaceRoot: string): Promise<ToolInvocation> {
    const configured = vscode.workspace.getConfiguration("copeland").get<string>("tsclPath", "").trim();
    if (configured) {
        return { command: configured, arguments: [], source: "copeland.tsclPath" };
    }

    const testCommand = process.env.COPLAND_VSCODE_TEST_TSCL_PATH?.trim();
    if (testCommand) {
        return { command: testCommand, arguments: [], source: "integration test" };
    }

    const manifests = [
        path.join(workspaceRoot, "dotnet-tools.json"),
        path.join(workspaceRoot, ".config", "dotnet-tools.json")
    ];
    for (const manifest of manifests) {
        try {
            const contents = await vscode.workspace.fs.readFile(vscode.Uri.file(manifest));
            const parsed = JSON.parse(Buffer.from(contents).toString("utf8"));
            if (parsed.tools?.["copeland.ts.tool"] || parsed.tools?.["Copeland.TS.Tool"]) {
                return {
                    command: "dotnet",
                    arguments: ["tool", "run", "tscl", "--"],
                    source: path.relative(workspaceRoot, manifest)
                };
            }
        } catch {
            // A missing or unrelated tool manifest is normal.
        }
    }

    return { command: "tscl", arguments: [], source: "global PATH" };
}

export async function runTool(tool: ToolInvocation, args: string[], cwd: string, output: vscode.OutputChannel): Promise<CommandResult> {
    const commandArgs = [...tool.arguments, ...args];
    output.appendLine(`[tool] selected ${tool.command} from ${tool.source}`);
    output.appendLine(`> ${tool.command} ${commandArgs.join(" ")}`);
    return new Promise<CommandResult>((resolve) => {
        const child = spawn(tool.command, commandArgs, { cwd, shell: false, windowsHide: true });
        child.stdout.on("data", (data: Buffer) => output.append(data.toString()));
        child.stderr.on("data", (data: Buffer) => output.append(data.toString()));
        child.on("error", (error) => {
            output.appendLine(error.message);
            resolve({ exitCode: -1, output: error.message });
        });
        child.on("close", (exitCode) => resolve({ exitCode: exitCode ?? -1, output: "" }));
    });
}

export async function queryServerVersion(tool: ToolInvocation, cwd: string): Promise<string> {
    return new Promise<string>((resolve, reject) => {
        execFile(tool.command, [...tool.arguments, "language-server", "--version"], { cwd, windowsHide: true }, (error, stdout, stderr) => {
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

export function spawnLanguageServer(tool: ToolInvocation, cwd: string, output: vscode.OutputChannel): ChildProcess {
    const child = spawn(tool.command, [...tool.arguments, "language-server"], { cwd, shell: false, windowsHide: true });
    child.stderr.on("data", (data: Buffer) => output.append(data.toString()));
    child.on("error", (error) => output.appendLine(`[launch] ${error.message}`));
    return child;
}

/**
 * Reaps the process launched by this extension, including the `dotnet tool`
 * wrapper's language-server child. This is intentionally never used for a
 * process discovered by name or PID outside this launch path.
 */
export async function terminateOwnedProcessTree(child: ChildProcess, output: vscode.OutputChannel): Promise<void> {
    if (!child.pid || child.exitCode !== null || child.killed) {
        return;
    }

    output.appendLine(`[language server] terminating owned process tree ${child.pid}`);
    if (process.platform === "win32") {
        await new Promise<void>((resolve) => {
            const killer = spawn("taskkill.exe", ["/pid", String(child.pid), "/T", "/F"], {
                shell: false,
                windowsHide: true
            });
            killer.once("error", () => resolve());
            killer.once("close", () => resolve());
        });
        await waitForProcessExit(child, 5000);
        return;
    }

    child.kill("SIGTERM");
    await waitForProcessExit(child, 5000);
}

function waitForProcessExit(child: ChildProcess, timeoutMs: number): Promise<void> {
    if (child.exitCode !== null) {
        return Promise.resolve();
    }

    return new Promise<void>((resolve) => {
        const timer = setTimeout(() => {
            child.removeListener("close", onClose);
            resolve();
        }, timeoutMs);
        const onClose = () => {
            clearTimeout(timer);
            resolve();
        };
        child.once("close", onClose);
    });
}

export async function readProjectVersion(projectPath: string | undefined): Promise<string | undefined> {
    if (!projectPath) {
        return undefined;
    }

    try {
        const bytes = await vscode.workspace.fs.readFile(vscode.Uri.file(projectPath));
        const project = Buffer.from(bytes).toString("utf8");
        const packageReference = /<PackageReference\s+[^>]*(?:Include|Update)="Copeland(?:\.TS)?(?:\.Sdk)?"[^>]*Version="([^"]+)"/i.exec(project);
        const property = /<Copeland(?:Ts|Toolchain)?Version>\s*([^<\s]+)\s*<\/Copeland(?:Ts|Toolchain)?Version>/i.exec(project);
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
