import * as path from "path";
import { ChildProcess } from "child_process";
import * as vscode from "vscode";
import { LanguageClient, LanguageClientOptions, ServerOptions, StreamInfo, Trace } from "vscode-languageclient/node";
import { CopelandOwner, OwnershipFileEntry, OwnershipMap } from "./ownershipMap";
import { isCompatibleVersion, projectPath, queryServerVersion, querySourceProfile, SourceProfileResult, readProjectVersion, resolveTscl, runTool, spawnLanguageServer, terminateOwnedProcessTree } from "./toolchain";

const ownershipRelativePath = "obj/copeland/workspace/editor-ownership.generated.json";
const manifestFileName = "tsconfig.tsx";
const tspackManifestFileName = "manifest.tsx";
const generatedNugetSource = "src/generated/Nuget.ts";
const ignoredSourceDirectories = new Set([".git", ".tspack", "bin", "dist", "node_modules", "obj"]);

export type WorkspaceState = "ready" | "missing-metadata" | "language-server-unavailable" | "version-mismatch";

export class WorkspaceController implements vscode.Disposable {
    private projectRootPath: string;
    private hasWorkspace = false;
    private hasTspackManifest = false;
    private disposed = false;
    private ownership: OwnershipMap | undefined;
    private ownershipContents: string | undefined;
    private client: LanguageClient | undefined;
    private languageServerProcess: ChildProcess | undefined;
    private startingLanguageServer: Promise<void> | undefined;
    private state: WorkspaceState = "missing-metadata";
    private readonly disposables: vscode.Disposable[] = [];
    private readonly originalLanguageIds = new Map<string, string>();
    private missingMetadataShown = false;
    private versionIssueShown = false;
    private reloadTimer: NodeJS.Timeout | undefined;
    private syncTimer: NodeJS.Timeout | undefined;
    private pendingSync: Promise<boolean> | undefined;
    private ownershipReload: Promise<void> = Promise.resolve();
    private readonly profiles = new Map<string, { version: number; owner?: string; result: SourceProfileResult }>();
    private readonly profileDiagnostics = vscode.languages.createDiagnosticCollection("copeland-profile");
    private readonly profileTimers = new Map<string, NodeJS.Timeout>();
    private serverVersion: string | undefined;
    private toolDescription: string | undefined;

    public constructor(
        public readonly folder: vscode.WorkspaceFolder,
        private readonly output: vscode.OutputChannel,
        private readonly onDidChange: () => void,
        private readonly extensionVersion: string) {
        this.projectRootPath = folder.uri.fsPath;
    }

    public get rootPath(): string {
        return this.projectRootPath;
    }

    public get manifestUri(): vscode.Uri {
        return vscode.Uri.file(path.join(this.rootPath, manifestFileName));
    }

    public get ownershipUri(): vscode.Uri {
        return vscode.Uri.file(path.join(this.rootPath, ...ownershipRelativePath.split("/")));
    }

    public get currentState(): WorkspaceState {
        return this.state;
    }

    public describeProject(): string {
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

    public async initialize(): Promise<void> {
        const rootManifest = this.manifestUri;
        const manifests = await this.exists(rootManifest) ? [rootManifest] : await vscode.workspace.findFiles(
            new vscode.RelativePattern(this.folder, `**/${manifestFileName}`),
            "**/{node_modules,bin,obj}/**",
            2);
        const chosenManifest = manifests.length === 1 ? manifests[0] : undefined;
        if (chosenManifest) {
            this.projectRootPath = path.dirname(chosenManifest.fsPath);
            this.hasWorkspace = true;
            this.output.appendLine(`[project] ${this.rootPath}`);
        } else if (manifests.length > 1) {
            this.output.appendLine("[project] multiple tsconfig.tsx files found; open the intended project folder.");
        }
        if (!this.hasWorkspace) {
            return;
        }

        const manifestWatcher = vscode.workspace.createFileSystemWatcher(
            new vscode.RelativePattern(this.rootPath, "{tsconfig,manifest}.tsx"));
        manifestWatcher.onDidChange(() => this.scheduleSync());
        manifestWatcher.onDidCreate(() => this.scheduleSync());
        manifestWatcher.onDidDelete(() => this.scheduleSync());
        this.disposables.push(manifestWatcher);

        const sources = vscode.workspace.createFileSystemWatcher(
            new vscode.RelativePattern(this.rootPath, "**/*.{ts,tsx}"), false, true, false);
        const sourceChanged = (uri: vscode.Uri) => {
            const parts = path.relative(this.rootPath, uri.fsPath).split(path.sep);
            if (!parts.some((part) => ignoredSourceDirectories.has(part))) {
                this.scheduleSync();
            }
        };
        sources.onDidCreate(sourceChanged);
        sources.onDidDelete(sourceChanged);
        this.disposables.push(sources);

        const bindingWatcher = vscode.workspace.createFileSystemWatcher(
            new vscode.RelativePattern(this.rootPath, generatedNugetSource), true, false, true);
        bindingWatcher.onDidChange(() => this.scheduleReload());
        this.disposables.push(bindingWatcher);

        const descriptorWatcher = vscode.workspace.createFileSystemWatcher(
            new vscode.RelativePattern(this.rootPath, ".tspack/build-manifests/*.request.json"));
        descriptorWatcher.onDidCreate(() => this.scheduleReload());
        descriptorWatcher.onDidChange(() => this.scheduleReload());
        descriptorWatcher.onDidDelete(() => this.scheduleReload());
        this.disposables.push(descriptorWatcher);

        const ownershipWatcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(this.rootPath, ownershipRelativePath));
        ownershipWatcher.onDidChange(() => this.scheduleReload());
        ownershipWatcher.onDidCreate(() => this.scheduleReload());
        ownershipWatcher.onDidDelete(() => this.scheduleReload());
        this.disposables.push(ownershipWatcher);

        this.disposables.push(vscode.workspace.onDidOpenTextDocument((document) => void this.routeDocument(document)));
        this.disposables.push(this.profileDiagnostics);
        this.disposables.push(vscode.workspace.onDidChangeTextDocument((event) => {
            if (!this.belongsToWorkspace(event.document.uri) || !isTypeScriptDocument(event.document)) {
                return;
            }
            const key = event.document.uri.toString();
            const previous = this.profileTimers.get(key);
            if (previous) {
                clearTimeout(previous);
            }
            this.profileTimers.set(key, setTimeout(() => {
                this.profileTimers.delete(key);
                void this.routeDocument(event.document);
            }, 250));
        }));
        this.disposables.push(vscode.workspace.onDidCloseTextDocument((document) => {
            this.profiles.delete(document.uri.toString());
            this.profileDiagnostics.delete(document.uri);
        }));
        this.disposables.push(vscode.workspace.onDidSaveTextDocument((document) => {
            if (this.belongsToWorkspace(document.uri) && isTypeScriptDocument(document)
                && (document.getText().includes("CopelandProfile") || this.ownerFor(document)?.profile)) {
                this.scheduleSync();
            }
        }));
        if (this.autoSync() || !await this.exists(this.ownershipUri)) {
            await this.syncWorkspace();
        } else {
            await this.reloadOwnership();
        }
    }

    public async syncWorkspace(): Promise<boolean> {
        if (!this.hasWorkspace || this.disposed) {
            return false;
        }
        if (this.pendingSync) {
            return this.pendingSync;
        }
        this.pendingSync = this.syncWorkspaceCore();
        try {
            return await this.pendingSync;
        } finally {
            this.pendingSync = undefined;
        }
    }

    private async syncWorkspaceCore(): Promise<boolean> {
        const result = await runTool(await resolveTscl(this.rootPath), ["workspace", "sync"], this.rootPath, this.output);
        await this.reloadOwnership();
        return result.exitCode === 0;
    }

    public reloadOwnership(): Promise<void> {
        const reload = this.ownershipReload.then(() => this.reloadOwnershipCore());
        this.ownershipReload = reload.catch(() => undefined);
        return reload;
    }

    private async reloadOwnershipCore(): Promise<void> {
        if (!this.hasWorkspace || this.disposed) {
            return;
        }
        this.hasTspackManifest = await this.exists(vscode.Uri.file(path.join(this.rootPath, tspackManifestFileName)));
        try {
            const content = await vscode.workspace.fs.readFile(this.ownershipUri);
            const ownershipContents = Buffer.from(content).toString("utf8");
            const metadata = JSON.parse(ownershipContents);
            this.ownership = new OwnershipMap(metadata);
            this.ownershipContents = ownershipContents;
            this.missingMetadataShown = false;
            await Promise.all(vscode.workspace.textDocuments.map((document) => this.routeDocument(document)));

            if (this.disposed) {
                return;
            }
            if (this.client) {
                await this.client.sendNotification("workspace/didChangeWatchedFiles", {
                    changes: [{ uri: this.ownershipUri.toString(), type: 2 }]
                });
            } else {
                await this.startLanguageServer();
            }
            this.onDidChange();
        } catch (error) {
            this.ownership = undefined;
            this.ownershipContents = undefined;
            this.state = "missing-metadata";
            await Promise.all(vscode.workspace.textDocuments.map((document) => this.routeDocument(document)));
            this.showMissingMetadataOnce(error);
            this.onDidChange();
        }
    }

    public ownerFor(document: vscode.TextDocument): OwnershipFileEntry | undefined {
        return this.ownerForUri(document.uri);
    }

    public ownerForUri(uri: vscode.Uri): OwnershipFileEntry | undefined {
        if (!this.belongsToWorkspace(uri) || !this.ownership) {
            return undefined;
        }

        return this.ownership.get(path.relative(this.rootPath, uri.fsPath));
    }

    public async restartLanguageServer(): Promise<void> {
        await this.stopLanguageServer("explicit restart");
        await this.startLanguageServer();
        this.onDidChange();
    }

    public async routeDocument(document: vscode.TextDocument): Promise<void> {
        if (this.disposed || !this.belongsToWorkspace(document.uri) || !isTypeScriptDocument(document)) {
            return;
        }

        const isManifest = this.isManifest(document.uri);
        const owner = this.ownerFor(document);
        const profile = isManifest || owner ? undefined : await this.readProfile(document);
        if (document.isClosed) {
            return;
        }
        const effectiveOwner = owner?.owner ?? (profile?.success ? profile.owner : undefined);
        const needsCopelandLanguage = isManifest || effectiveOwner === "tscl";
        const targetLanguage = copelandLanguageId(document.uri);

        if (needsCopelandLanguage) {
            if (!isCopelandLanguage(document.languageId)) {
                this.originalLanguageIds.set(document.uri.toString(), document.languageId);
                await vscode.languages.setTextDocumentLanguage(document, targetLanguage);
            } else if (!isManifest) {
                await this.readProfile(document, owner?.owner);
            }
            this.onDidChange();
            return;
        }

        if (isCopelandLanguage(document.languageId)) {
            const original = this.originalLanguageIds.get(document.uri.toString()) ?? builtInTypeScriptLanguageId(document.uri);
            this.originalLanguageIds.delete(document.uri.toString());
            await vscode.languages.setTextDocumentLanguage(document, original);
        } else if (owner) {
            await this.readProfile(document, owner.owner);
        }
        this.onDidChange();
    }

    private async readProfile(document: vscode.TextDocument, owner?: string): Promise<SourceProfileResult | undefined> {
        const key = document.uri.toString();
        const text = document.getText();
        if (!text.includes("CopelandProfile")) {
            this.profiles.delete(key);
            this.profileDiagnostics.delete(document.uri);
            return undefined;
        }
        const cached = this.profiles.get(key);
        if (cached?.version === document.version && cached.owner === owner) {
            return cached.result;
        }
        const version = document.version;
        try {
            const result = await querySourceProfile(await resolveTscl(this.rootPath), document.uri.fsPath, text, this.rootPath, owner);
            if (document.isClosed || document.version !== version) {
                return undefined;
            }
            this.profiles.set(key, { version, owner, result });
            // The Copeland LSP already publishes these errors for its owned files.
            const visibleDiagnostics = owner === "tscl" && this.state === "ready" ? [] : result.diagnostics;
            this.profileDiagnostics.set(document.uri, visibleDiagnostics.map((item) => {
                const range = new vscode.Range(document.positionAt(item.position), document.positionAt(item.position + item.length));
                const diagnostic = new vscode.Diagnostic(range, item.message, vscode.DiagnosticSeverity.Error);
                diagnostic.code = item.code;
                diagnostic.source = "tscl-profile";
                return diagnostic;
            }));
            return result;
        } catch (error) {
            this.output.appendLine(`[profile] ${error instanceof Error ? error.message : String(error)}`);
            return undefined;
        }
    }

    public describeActiveDocument(document: vscode.TextDocument | undefined): string | undefined {
        if (!document || !this.belongsToWorkspace(document.uri) || !isTypeScriptDocument(document)) {
            return undefined;
        }

        if (this.isManifest(document.uri)) {
            return "Copeland TS: tscl (workspace manifest)";
        }

        const owner = this.ownerFor(document);
        const profile = this.profiles.get(document.uri.toString())?.result.profile ?? owner?.profile;
        const profileLabel = profile ? ` (${profile})` : "";
        if (owner?.owner === "tscl") {
            return this.state === "ready" ? `Copeland TS: tscl${profileLabel}` : `Copeland TS: ${this.state.replace(/-/g, " ")}${profileLabel}`;
        }

        if (owner?.owner === "tsc") {
            return `TypeScript: tsc${profileLabel}`;
        }

        return undefined;
    }

    public async dispose(): Promise<void> {
        this.disposed = true;
        for (const timer of this.profileTimers.values()) {
            clearTimeout(timer);
        }
        if (this.reloadTimer) {
            clearTimeout(this.reloadTimer);
        }
        if (this.syncTimer) {
            clearTimeout(this.syncTimer);
        }

        await this.stopLanguageServer("workspace disposal");
        for (const disposable of this.disposables) {
            disposable.dispose();
        }
    }

    private async startLanguageServer(): Promise<void> {
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
        } finally {
            this.startingLanguageServer = undefined;
        }
    }

    private async launchLanguageServer(): Promise<void> {
        const ownership = this.ownership;
        if (!ownership) {
            this.state = "missing-metadata";
            return;
        }

        const tool = await resolveTscl(this.rootPath);
        this.toolDescription = `${tool.command} (${tool.source})`;
        try {
            this.output.appendLine(`[language server] checking ${tool.command} (${tool.source})`);
            const serverVersion = await queryServerVersion(tool, this.rootPath);
            this.serverVersion = serverVersion;
            this.output.appendLine(`[language server] selected ${tool.command} from ${tool.source}; version ${serverVersion}`);
            const sampleProject = ownership.entriesFor("tscl")[0]?.project;
            const requiredProjectVersion = await readProjectVersion(projectPath(this.rootPath, sampleProject));
            this.output.appendLine("[language server] project compatibility checked");
            if (!isCompatibleVersion({ extension: this.extensionVersion, server: serverVersion, project: requiredProjectVersion })) {
                this.state = "version-mismatch";
                this.showVersionIssueOnce(serverVersion, requiredProjectVersion);
                return;
            }

            const serverOptions: ServerOptions = async (): Promise<StreamInfo> => {
                await this.stopOwnedLanguageServerProcess();
                const process = spawnLanguageServer(tool, this.rootPath, this.output);
                this.languageServerProcess = process;
                if (!process.stdout || !process.stdin) {
                    throw new Error("Copeland language server did not provide stdio streams.");
                }

                return { reader: process.stdout, writer: process.stdin };
            };
            const options: LanguageClientOptions = {
                documentSelector: [
                    { scheme: "file", language: "copeland-typescript" },
                    { scheme: "file", language: "copeland-typescriptreact" }
                ],
                workspaceFolder: this.folder,
                initializationOptions: {
                    workspaceRoot: this.rootPath,
                    ownershipFile: this.ownershipUri.fsPath,
                    project: sampleProject ? projectPath(this.rootPath, sampleProject) : undefined,
                    clientVersion: this.extensionVersion,
                    expectedServerVersion: this.extensionVersion,
                    loggingLevel: vscode.workspace.getConfiguration("copeland.languageServer").get<string>("trace", "off")
                },
                middleware: {
                    provideHover: async (document, position, token, next) => {
                        const hover = await next(document, position, token);
                        this.output.appendLine(
                            `[hover] ${path.relative(this.rootPath, document.uri.fsPath)}:${position.line + 1}:${position.character + 1} ${hover ? "resolved" : "no result"}`);
                        return hover;
                    }
                },
                outputChannel: this.output,
                traceOutputChannel: this.output
            };
            const client = new LanguageClient("copelandTs", "Copeland TS Language Server", serverOptions, options);
            client.setTrace(toTrace(vscode.workspace.getConfiguration("copeland.languageServer").get<string>("trace", "off")));
            await client.start();
            this.client = client;
            this.state = "ready";
            for (const document of vscode.workspace.textDocuments) {
                if (this.ownerFor(document)?.owner === "tscl") {
                    this.profileDiagnostics.delete(document.uri);
                }
            }
            this.output.appendLine(`[language server] ready (${serverVersion})`);
        } catch (error) {
            await this.stopOwnedLanguageServerProcess();
            this.serverVersion = undefined;
            this.state = "language-server-unavailable";
            this.output.appendLine(`[language server] ${error instanceof Error ? error.message : String(error)}`);
            vscode.window.showWarningMessage(
                `Copeland language server is unavailable. Run "dotnet tool install --global Copeland.TS.Tool --version ${this.extensionVersion}" or set copeland.tsclPath.`);
        }
    }

    private async stopLanguageServer(reason: string): Promise<void> {
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

    private async stopOwnedLanguageServerProcess(): Promise<void> {
        const process = this.languageServerProcess;
        this.languageServerProcess = undefined;
        if (process) {
            await terminateOwnedProcessTree(process, this.output);
        }
    }

    private scheduleReload(): void {
        if (this.reloadTimer) {
            clearTimeout(this.reloadTimer);
        }

        this.reloadTimer = setTimeout(() => void this.reloadOwnership(), 150);
    }

    private autoSync(): boolean {
        return vscode.workspace.getConfiguration("copeland.workspace", this.folder.uri).get<boolean>("autoSync", true);
    }

    private scheduleSync(): void {
        if (!this.autoSync()) {
            this.scheduleReload();
            return;
        }
        if (this.syncTimer) {
            clearTimeout(this.syncTimer);
        }
        this.syncTimer = setTimeout(async () => {
            await this.pendingSync;
            await this.syncWorkspace();
        }, 200);
    }

    private async exists(uri: vscode.Uri): Promise<boolean> {
        try {
            await vscode.workspace.fs.stat(uri);
            return true;
        } catch {
            return false;
        }
    }

    private isManifest(uri: vscode.Uri): boolean {
        const relative = path.relative(this.rootPath, uri.fsPath);
        return relative === manifestFileName || (this.hasTspackManifest && relative === tspackManifestFileName);
    }

    private belongsToWorkspace(uri: vscode.Uri): boolean {
        return uri.scheme === "file" && (uri.fsPath === this.rootPath || uri.fsPath.startsWith(this.rootPath + path.sep));
    }

    private showMissingMetadataOnce(error: unknown): void {
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

    private showVersionIssueOnce(serverVersion: string, projectVersion: string | undefined): void {
        if (this.versionIssueShown) {
            return;
        }

        this.versionIssueShown = true;
        const required = projectVersion ?? this.extensionVersion;
        void vscode.window.showWarningMessage(`This project requires Copeland TS ${required}. Installed language server: ${serverVersion}. Update the Copeland toolchain.`);
    }
}

function isTypeScriptDocument(document: vscode.TextDocument): boolean {
    return document.uri.scheme === "file" && [".ts", ".tsx"].includes(path.extname(document.uri.fsPath).toLocaleLowerCase());
}

function copelandLanguageId(uri: vscode.Uri): string {
    return path.extname(uri.fsPath).toLocaleLowerCase() === ".tsx" ? "copeland-typescriptreact" : "copeland-typescript";
}

function builtInTypeScriptLanguageId(uri: vscode.Uri): string {
    return path.extname(uri.fsPath).toLocaleLowerCase() === ".tsx" ? "typescriptreact" : "typescript";
}

function isCopelandLanguage(languageId: string): boolean {
    return languageId === "copeland-typescript" || languageId === "copeland-typescriptreact";
}

function toTrace(value: string): Trace {
    switch (value) {
        case "messages": return Trace.Messages;
        case "verbose": return Trace.Verbose;
        default: return Trace.Off;
    }
}
