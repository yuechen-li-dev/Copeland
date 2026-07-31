import * as path from "path";
import { ChildProcess } from "child_process";
import * as vscode from "vscode";
import { LanguageClient, LanguageClientOptions, ServerOptions, StreamInfo, Trace } from "vscode-languageclient/node";
import { CopelandOwner, OwnershipFileEntry, OwnershipMap } from "./ownershipMap";
import { isCompatibleVersion, projectPath, queryServerVersion, readProjectVersion, resolveTscl, runTool, spawnLanguageServer, terminateOwnedProcessTree } from "./toolchain";

const ownershipRelativePath = "obj/copeland/workspace/editor-ownership.generated.json";
const manifestFileName = "tsconfig.tsx";

export type WorkspaceState = "ready" | "missing-metadata" | "language-server-unavailable" | "version-mismatch";

export class WorkspaceController implements vscode.Disposable {
    private projectRootPath: string;
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
    private ownershipReload: Promise<void> = Promise.resolve();
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
        const manifests = await vscode.workspace.findFiles(
            new vscode.RelativePattern(this.folder, `**/${manifestFileName}`),
            "**/{node_modules,bin,obj}/**",
            2);
        if (manifests.length === 1) {
            this.projectRootPath = path.dirname(manifests[0].fsPath);
            this.output.appendLine(`[project] ${this.rootPath}`);
        } else if (manifests.length > 1) {
            this.output.appendLine("[project] multiple tsconfig.tsx files found; open the intended project folder.");
        }

        const manifestPattern = new vscode.RelativePattern(this.rootPath, manifestFileName);
        this.disposables.push(vscode.workspace.createFileSystemWatcher(manifestPattern));
        const manifestWatcher = this.disposables[this.disposables.length - 1] as vscode.FileSystemWatcher;
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

    public reloadOwnership(): Promise<void> {
        const reload = this.ownershipReload.then(() => this.reloadOwnershipCore());
        this.ownershipReload = reload.catch(() => undefined);
        return reload;
    }

    private async reloadOwnershipCore(): Promise<void> {
        try {
            const content = await vscode.workspace.fs.readFile(this.ownershipUri);
            const ownershipContents = Buffer.from(content).toString("utf8");
            const metadata = JSON.parse(ownershipContents);
            this.ownership = new OwnershipMap(metadata);
            this.ownershipContents = ownershipContents;
            this.missingMetadataShown = false;
            await Promise.all(vscode.workspace.textDocuments.map((document) => this.routeDocument(document)));

            await this.startLanguageServer();
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
        if (!this.belongsToWorkspace(document.uri) || !this.ownership) {
            return undefined;
        }

        return this.ownership.get(path.relative(this.rootPath, document.uri.fsPath));
    }

    public async restartLanguageServer(): Promise<void> {
        await this.stopLanguageServer("explicit restart");
        await this.startLanguageServer();
        this.onDidChange();
    }

    public async routeDocument(document: vscode.TextDocument): Promise<void> {
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

    public describeActiveDocument(document: vscode.TextDocument | undefined): string | undefined {
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

    public async dispose(): Promise<void> {
        if (this.reloadTimer) {
            clearTimeout(this.reloadTimer);
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

    private async handleManifestChange(): Promise<void> {
        if (vscode.workspace.getConfiguration("copeland.workspace", this.folder.uri).get<boolean>("autoSync", true)) {
            await vscode.commands.executeCommand("copeland.workspaceSync", this.folder.uri);
        }
    }

    private async ensureOwnership(): Promise<void> {
        try {
            await vscode.workspace.fs.stat(this.ownershipUri);
            return;
        } catch {
            const tool = await resolveTscl(this.rootPath);
            this.output.appendLine("[ownership] generating canonical ownership from tsconfig.tsx");
            await runTool(tool, ["workspace", "sync"], this.rootPath, this.output);
        }
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
