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
exports.WorkspaceController = void 0;
const path = __importStar(require("path"));
const vscode = __importStar(require("vscode"));
const node_1 = require("vscode-languageclient/node");
const ownershipMap_1 = require("./ownershipMap");
const toolchain_1 = require("./toolchain");
const ownershipRelativePath = "obj/copeland/workspace/editor-ownership.generated.json";
const manifestFileName = "tsconfig.tsx";
class WorkspaceController {
    folder;
    output;
    onDidChange;
    extensionVersion;
    projectRootPath;
    ownership;
    ownershipContents;
    client;
    languageServerProcess;
    startingLanguageServer;
    state = "missing-metadata";
    disposables = [];
    originalLanguageIds = new Map();
    missingMetadataShown = false;
    versionIssueShown = false;
    reloadTimer;
    ownershipReload = Promise.resolve();
    serverVersion;
    toolDescription;
    constructor(folder, output, onDidChange, extensionVersion) {
        this.folder = folder;
        this.output = output;
        this.onDidChange = onDidChange;
        this.extensionVersion = extensionVersion;
        this.projectRootPath = folder.uri.fsPath;
    }
    get rootPath() {
        return this.projectRootPath;
    }
    get manifestUri() {
        return vscode.Uri.file(path.join(this.rootPath, manifestFileName));
    }
    get ownershipUri() {
        return vscode.Uri.file(path.join(this.rootPath, ...ownershipRelativePath.split("/")));
    }
    get currentState() {
        return this.state;
    }
    describeProject() {
        const tsclFiles = this.ownership?.entriesFor("tscl").length ?? 0;
        const tscFiles = this.ownership?.entriesFor("tsc").length ?? 0;
        const version = this.serverVersion ?? "not running";
        const tool = this.toolDescription ?? "not resolved";
        return [
            `Project: ${this.rootPath}`,
            `Manifest: ${this.manifestUri.fsPath}`,
            `Ownership: ${tsclFiles} tscl file(s), ${tscFiles} tsc file(s)`,
            `Language server: ${version}`,
            `Tool: ${tool}`,
            `State: ${this.state}`
        ].join("\n");
    }
    async initialize() {
        const manifests = await vscode.workspace.findFiles(new vscode.RelativePattern(this.folder, `**/${manifestFileName}`), "**/{node_modules,bin,obj}/**", 2);
        if (manifests.length === 1) {
            this.projectRootPath = path.dirname(manifests[0].fsPath);
            this.output.appendLine(`[project] ${this.rootPath}`);
        }
        else if (manifests.length > 1) {
            this.output.appendLine("[project] multiple tsconfig.tsx files found; open the intended project folder.");
        }
        const manifestPattern = new vscode.RelativePattern(this.rootPath, manifestFileName);
        this.disposables.push(vscode.workspace.createFileSystemWatcher(manifestPattern));
        const manifestWatcher = this.disposables[this.disposables.length - 1];
        manifestWatcher.onDidChange(() => void this.handleManifestChange());
        const ownershipWatcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(this.rootPath, ownershipRelativePath));
        ownershipWatcher.onDidChange(() => this.scheduleReload());
        ownershipWatcher.onDidCreate(() => this.scheduleReload());
        ownershipWatcher.onDidDelete(() => this.scheduleReload());
        this.disposables.push(ownershipWatcher);
        this.disposables.push(vscode.workspace.onDidOpenTextDocument((document) => void this.routeDocument(document)));
        await this.ensureOwnership();
        await this.reloadOwnership();
    }
    reloadOwnership() {
        const reload = this.ownershipReload.then(() => this.reloadOwnershipCore());
        this.ownershipReload = reload.catch(() => undefined);
        return reload;
    }
    async reloadOwnershipCore() {
        try {
            const content = await vscode.workspace.fs.readFile(this.ownershipUri);
            const ownershipContents = Buffer.from(content).toString("utf8");
            const metadata = JSON.parse(ownershipContents);
            this.ownership = new ownershipMap_1.OwnershipMap(metadata);
            this.ownershipContents = ownershipContents;
            this.missingMetadataShown = false;
            await Promise.all(vscode.workspace.textDocuments.map((document) => this.routeDocument(document)));
            await this.startLanguageServer();
            this.onDidChange();
        }
        catch (error) {
            this.ownership = undefined;
            this.ownershipContents = undefined;
            this.state = "missing-metadata";
            await Promise.all(vscode.workspace.textDocuments.map((document) => this.routeDocument(document)));
            this.showMissingMetadataOnce(error);
            this.onDidChange();
        }
    }
    ownerFor(document) {
        if (!this.belongsToWorkspace(document.uri) || !this.ownership) {
            return undefined;
        }
        return this.ownership.get(path.relative(this.rootPath, document.uri.fsPath));
    }
    async restartLanguageServer() {
        await this.stopLanguageServer("explicit restart");
        await this.startLanguageServer();
        this.onDidChange();
    }
    async routeDocument(document) {
        if (!this.belongsToWorkspace(document.uri) || !isTypeScriptDocument(document)) {
            return;
        }
        const isManifest = path.basename(document.uri.fsPath).toLocaleLowerCase() === manifestFileName;
        const owner = this.ownerFor(document);
        const needsCopelandLanguage = isManifest || owner?.owner === "tscl";
        const targetLanguage = copelandLanguageId(document.uri);
        if (needsCopelandLanguage) {
            if (!isCopelandLanguage(document.languageId)) {
                this.originalLanguageIds.set(document.uri.toString(), document.languageId);
                await vscode.languages.setTextDocumentLanguage(document, targetLanguage);
            }
            return;
        }
        if (isCopelandLanguage(document.languageId)) {
            const original = this.originalLanguageIds.get(document.uri.toString()) ?? builtInTypeScriptLanguageId(document.uri);
            this.originalLanguageIds.delete(document.uri.toString());
            await vscode.languages.setTextDocumentLanguage(document, original);
        }
    }
    describeActiveDocument(document) {
        if (!document || !this.belongsToWorkspace(document.uri) || !isTypeScriptDocument(document)) {
            return undefined;
        }
        if (path.basename(document.uri.fsPath).toLocaleLowerCase() === manifestFileName) {
            return "Copeland TS: tscl (workspace manifest)";
        }
        const owner = this.ownerFor(document);
        if (owner?.owner === "tscl") {
            return this.state === "ready" ? "Copeland TS: tscl" : `Copeland TS: ${this.state.replace(/-/g, " ")}`;
        }
        if (owner?.owner === "tsc") {
            return "TypeScript: tsc";
        }
        return undefined;
    }
    async dispose() {
        if (this.reloadTimer) {
            clearTimeout(this.reloadTimer);
        }
        await this.stopLanguageServer("workspace disposal");
        for (const disposable of this.disposables) {
            disposable.dispose();
        }
    }
    async startLanguageServer() {
        if (this.client || !this.ownership || this.ownership.entriesFor("tscl").length === 0) {
            this.state = this.ownership ? "ready" : this.state;
            return;
        }
        if (this.startingLanguageServer) {
            await this.startingLanguageServer;
            return;
        }
        this.startingLanguageServer = this.launchLanguageServer();
        try {
            await this.startingLanguageServer;
        }
        finally {
            this.startingLanguageServer = undefined;
        }
    }
    async launchLanguageServer() {
        const ownership = this.ownership;
        if (!ownership) {
            this.state = "missing-metadata";
            return;
        }
        const tool = await (0, toolchain_1.resolveTscl)(this.rootPath);
        this.toolDescription = `${tool.command} (${tool.source})`;
        try {
            this.output.appendLine(`[language server] checking ${tool.command} (${tool.source})`);
            const serverVersion = await (0, toolchain_1.queryServerVersion)(tool, this.rootPath);
            this.serverVersion = serverVersion;
            this.output.appendLine(`[language server] selected ${tool.command} from ${tool.source}; version ${serverVersion}`);
            const sampleProject = ownership.entriesFor("tscl")[0]?.project;
            const requiredProjectVersion = await (0, toolchain_1.readProjectVersion)((0, toolchain_1.projectPath)(this.rootPath, sampleProject));
            this.output.appendLine("[language server] project compatibility checked");
            if (!(0, toolchain_1.isCompatibleVersion)({ extension: this.extensionVersion, server: serverVersion, project: requiredProjectVersion })) {
                this.state = "version-mismatch";
                this.showVersionIssueOnce(serverVersion, requiredProjectVersion);
                return;
            }
            const serverOptions = async () => {
                await this.stopOwnedLanguageServerProcess();
                const process = (0, toolchain_1.spawnLanguageServer)(tool, this.rootPath, this.output);
                this.languageServerProcess = process;
                if (!process.stdout || !process.stdin) {
                    throw new Error("Copeland language server did not provide stdio streams.");
                }
                return { reader: process.stdout, writer: process.stdin };
            };
            const options = {
                documentSelector: [
                    { scheme: "file", language: "copeland-typescript" },
                    { scheme: "file", language: "copeland-typescriptreact" }
                ],
                workspaceFolder: this.folder,
                initializationOptions: {
                    workspaceRoot: this.rootPath,
                    ownershipFile: this.ownershipUri.fsPath,
                    project: sampleProject ? (0, toolchain_1.projectPath)(this.rootPath, sampleProject) : undefined,
                    clientVersion: this.extensionVersion,
                    expectedServerVersion: this.extensionVersion,
                    loggingLevel: vscode.workspace.getConfiguration("copeland.languageServer").get("trace", "off")
                },
                middleware: {
                    provideHover: async (document, position, token, next) => {
                        const hover = await next(document, position, token);
                        this.output.appendLine(`[hover] ${path.relative(this.rootPath, document.uri.fsPath)}:${position.line + 1}:${position.character + 1} ${hover ? "resolved" : "no result"}`);
                        return hover;
                    }
                },
                outputChannel: this.output,
                traceOutputChannel: this.output
            };
            const client = new node_1.LanguageClient("copelandTs", "Copeland TS Language Server", serverOptions, options);
            client.setTrace(toTrace(vscode.workspace.getConfiguration("copeland.languageServer").get("trace", "off")));
            await client.start();
            this.client = client;
            this.state = "ready";
            this.output.appendLine(`[language server] ready (${serverVersion})`);
        }
        catch (error) {
            await this.stopOwnedLanguageServerProcess();
            this.serverVersion = undefined;
            this.state = "language-server-unavailable";
            this.output.appendLine(`[language server] ${error instanceof Error ? error.message : String(error)}`);
            vscode.window.showWarningMessage(`Copeland language server is unavailable. Run "dotnet tool install --global Copeland.TS.Tool --version ${this.extensionVersion}" or set copeland.tsclPath.`);
        }
    }
    async stopLanguageServer(reason) {
        if (this.startingLanguageServer) {
            await this.startingLanguageServer;
        }
        const client = this.client;
        this.client = undefined;
        if (client) {
            this.output.appendLine(`[language server] stopping: ${reason}`);
            await client.stop();
        }
        await this.stopOwnedLanguageServerProcess();
    }
    async stopOwnedLanguageServerProcess() {
        const process = this.languageServerProcess;
        this.languageServerProcess = undefined;
        if (process) {
            await (0, toolchain_1.terminateOwnedProcessTree)(process, this.output);
        }
    }
    scheduleReload() {
        if (this.reloadTimer) {
            clearTimeout(this.reloadTimer);
        }
        this.reloadTimer = setTimeout(() => void this.reloadOwnership(), 150);
    }
    async handleManifestChange() {
        if (vscode.workspace.getConfiguration("copeland.workspace", this.folder.uri).get("autoSync", true)) {
            await vscode.commands.executeCommand("copeland.workspaceSync", this.folder.uri);
        }
    }
    async ensureOwnership() {
        try {
            await vscode.workspace.fs.stat(this.ownershipUri);
            return;
        }
        catch {
            const tool = await (0, toolchain_1.resolveTscl)(this.rootPath);
            this.output.appendLine("[ownership] generating canonical ownership from tsconfig.tsx");
            await (0, toolchain_1.runTool)(tool, ["workspace", "sync"], this.rootPath, this.output);
        }
    }
    belongsToWorkspace(uri) {
        return uri.scheme === "file" && (uri.fsPath === this.rootPath || uri.fsPath.startsWith(this.rootPath + path.sep));
    }
    showMissingMetadataOnce(error) {
        this.output.appendLine(`[ownership] ${error instanceof Error ? error.message : String(error)}`);
        if (this.missingMetadataShown) {
            return;
        }
        this.missingMetadataShown = true;
        void vscode.window.showWarningMessage("Copeland could not resolve tsconfig.tsx ownership. See the Copeland TS output.", "Workspace Sync").then((action) => {
            if (action) {
                void vscode.commands.executeCommand("copeland.workspaceSync", this.folder.uri);
            }
        });
    }
    showVersionIssueOnce(serverVersion, projectVersion) {
        if (this.versionIssueShown) {
            return;
        }
        this.versionIssueShown = true;
        const required = projectVersion ?? this.extensionVersion;
        void vscode.window.showWarningMessage(`This project requires Copeland TS ${required}. Installed language server: ${serverVersion}. Update the Copeland toolchain.`);
    }
}
exports.WorkspaceController = WorkspaceController;
function isTypeScriptDocument(document) {
    return document.uri.scheme === "file" && [".ts", ".tsx"].includes(path.extname(document.uri.fsPath).toLocaleLowerCase());
}
function copelandLanguageId(uri) {
    return path.extname(uri.fsPath).toLocaleLowerCase() === ".tsx" ? "copeland-typescriptreact" : "copeland-typescript";
}
function builtInTypeScriptLanguageId(uri) {
    return path.extname(uri.fsPath).toLocaleLowerCase() === ".tsx" ? "typescriptreact" : "typescript";
}
function isCopelandLanguage(languageId) {
    return languageId === "copeland-typescript" || languageId === "copeland-typescriptreact";
}
function toTrace(value) {
    switch (value) {
        case "messages": return node_1.Trace.Messages;
        case "verbose": return node_1.Trace.Verbose;
        default: return node_1.Trace.Off;
    }
}
//# sourceMappingURL=workspaceController.js.map