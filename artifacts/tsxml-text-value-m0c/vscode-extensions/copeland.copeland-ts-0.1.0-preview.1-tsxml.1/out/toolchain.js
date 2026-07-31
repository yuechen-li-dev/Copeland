"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.resolveTscl = resolveTscl;
exports.runTool = runTool;
exports.queryServerVersion = queryServerVersion;
exports.isCompatibleVersion = isCompatibleVersion;
exports.spawnLanguageServer = spawnLanguageServer;
exports.terminateOwnedProcessTree = terminateOwnedProcessTree;
exports.readProjectVersion = readProjectVersion;
exports.projectPath = projectPath;
const child_process_1 = require("child_process");
const path = __importStar(require("path"));
const vscode = __importStar(require("vscode"));
async function resolveTscl(workspaceRoot) {
    const configured = vscode.workspace.getConfiguration("copeland").get("tsclPath", "").trim();
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
        }
        catch {
            // A missing or unrelated tool manifest is normal.
        }
    }
    return { command: "tscl", arguments: [], source: "global PATH" };
}
async function runTool(tool, args, cwd, output) {
    const commandArgs = [...tool.arguments, ...args];
    output.appendLine(`[tool] selected ${tool.command} from ${tool.source}`);
    output.appendLine(`> ${tool.command} ${commandArgs.join(" ")}`);
    return new Promise((resolve) => {
        const child = (0, child_process_1.spawn)(tool.command, commandArgs, { cwd, shell: false, windowsHide: true });
        child.stdout.on("data", (data) => output.append(data.toString()));
        child.stderr.on("data", (data) => output.append(data.toString()));
        child.on("error", (error) => {
            output.appendLine(error.message);
            resolve({ exitCode: -1, output: error.message });
        });
        child.on("close", (exitCode) => resolve({ exitCode: exitCode ?? -1, output: "" }));
    });
}
async function queryServerVersion(tool, cwd) {
    return new Promise((resolve, reject) => {
        (0, child_process_1.execFile)(tool.command, [...tool.arguments, "language-server", "--version"], { cwd, windowsHide: true }, (error, stdout, stderr) => {
            if (error) {
                reject(new Error(stderr.trim() || error.message));
                return;
            }
            resolve(stdout.trim());
        });
    });
}
function isCompatibleVersion(expected) {
    return [expected.server, expected.project].filter((version) => Boolean(version)).every((version) => sameMajorMinor(expected.extension, version));
}
function sameMajorMinor(left, right) {
    const leftParts = left.split(".");
    const rightParts = right.split(".");
    return leftParts.length >= 2 && rightParts.length >= 2 && leftParts[0] === rightParts[0] && leftParts[1] === rightParts[1];
}
function spawnLanguageServer(tool, cwd, output) {
    const child = (0, child_process_1.spawn)(tool.command, [...tool.arguments, "language-server"], { cwd, shell: false, windowsHide: true });
    child.stderr.on("data", (data) => output.append(data.toString()));
    child.on("error", (error) => output.appendLine(`[launch] ${error.message}`));
    return child;
}
/**
 * Reaps the process launched by this extension, including the `dotnet tool`
 * wrapper's language-server child. This is intentionally never used for a
 * process discovered by name or PID outside this launch path.
 */
async function terminateOwnedProcessTree(child, output) {
    if (!child.pid || child.exitCode !== null || child.killed) {
        return;
    }
    output.appendLine(`[language server] terminating owned process tree ${child.pid}`);
    if (process.platform === "win32") {
        await new Promise((resolve) => {
            const killer = (0, child_process_1.spawn)("taskkill.exe", ["/pid", String(child.pid), "/T", "/F"], {
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
function waitForProcessExit(child, timeoutMs) {
    if (child.exitCode !== null) {
        return Promise.resolve();
    }
    return new Promise((resolve) => {
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
async function readProjectVersion(projectPath) {
    if (!projectPath) {
        return undefined;
    }
    try {
        const bytes = await vscode.workspace.fs.readFile(vscode.Uri.file(projectPath));
        const project = Buffer.from(bytes).toString("utf8");
        const packageReference = /<PackageReference\s+[^>]*(?:Include|Update)="Copeland(?:\.TS)?(?:\.Sdk)?"[^>]*Version="([^"]+)"/i.exec(project);
        const property = /<Copeland(?:Ts|Toolchain)?Version>\s*([^<\s]+)\s*<\/Copeland(?:Ts|Toolchain)?Version>/i.exec(project);
        return packageReference?.[1] ?? property?.[1];
    }
    catch {
        return undefined;
    }
}
function projectPath(workspaceRoot, project) {
    if (!project) {
        return undefined;
    }
    return path.resolve(workspaceRoot, project);
}
//# sourceMappingURL=toolchain.js.map