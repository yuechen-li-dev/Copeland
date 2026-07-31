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
exports.activate = activate;
exports.deactivate = deactivate;
const path = __importStar(require("path"));
const vscode = __importStar(require("vscode"));
const workspaceController_1 = require("./workspaceController");
const toolchain_1 = require("./toolchain");
let controllers = [];
let statusBar;
let output;
let extensionVersion;
async function activate(context) {
    output = vscode.window.createOutputChannel("Copeland TS Language Server");
    extensionVersion = String(context.extension.packageJSON.version);
    statusBar = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
    statusBar.command = "copeland.showFileOwner";
    context.subscriptions.push(output, statusBar);
    controllers = (vscode.workspace.workspaceFolders ?? []).map((folder) => new workspaceController_1.WorkspaceController(folder, output, updateStatus, extensionVersion));
    context.subscriptions.push({ dispose: () => void disposeControllers() });
    registerCommands(context);
    context.subscriptions.push(vscode.window.onDidChangeActiveTextEditor(() => updateStatus()));
    context.subscriptions.push(vscode.workspace.onDidChangeWorkspaceFolders((event) => void refreshWorkspaceControllers(event)));
    await Promise.all(controllers.map((controller) => controller.initialize()));
    updateStatus();
}
async function deactivate() {
    await disposeControllers();
}
function registerCommands(context) {
    context.subscriptions.push(vscode.commands.registerCommand("copeland.workspaceSync", async (uri) => {
        const controller = controllerForUri(uri) ?? controllerForActiveDocument();
        if (!controller) {
            return;
        }
        const result = await (0, toolchain_1.runTool)(await (0, toolchain_1.resolveTscl)(controller.rootPath), ["workspace", "sync"], controller.rootPath, output);
        output.show(true);
        if (result.exitCode === 0) {
            await controller.reloadOwnership();
            vscode.window.showInformationMessage("Copeland workspace ownership synchronized.");
        }
        else {
            vscode.window.showErrorMessage("Copeland workspace sync failed. See Copeland TS Language Server output.");
        }
    }), vscode.commands.registerCommand("copeland.workspaceValidate", async (uri) => {
        const controller = controllerForUri(uri) ?? controllerForActiveDocument();
        if (!controller) {
            return;
        }
        const result = await (0, toolchain_1.runTool)(await (0, toolchain_1.resolveTscl)(controller.rootPath), ["workspace", "validate"], controller.rootPath, output);
        output.show(true);
        if (result.exitCode === 0) {
            vscode.window.showInformationMessage("Copeland workspace ownership is valid.");
        }
    }), vscode.commands.registerCommand("copeland.showFileOwner", async () => {
        const document = vscode.window.activeTextEditor?.document;
        const controller = controllerForActiveDocument();
        if (!document || !controller) {
            return;
        }
        const owner = controller.ownerFor(document);
        const message = owner
            ? `${path.relative(controller.rootPath, document.uri.fsPath)} is ${owner.owner}-owned by ${owner.matchedRule} (${owner.project}).`
            : path.basename(document.uri.fsPath) === "tsconfig.tsx"
                ? "tsconfig.tsx is served as the Copeland workspace manifest."
                : "This file is not listed in Copeland editor ownership metadata.";
        await vscode.window.showInformationMessage(message, "Workspace Sync", "Reload Ownership").then((action) => {
            if (action === "Workspace Sync") {
                return vscode.commands.executeCommand("copeland.workspaceSync", controller.folder.uri);
            }
            if (action === "Reload Ownership") {
                return vscode.commands.executeCommand("copeland.reloadWorkspaceOwnership", controller.folder.uri);
            }
            return undefined;
        });
    }), vscode.commands.registerCommand("copeland.buildProject", async () => runDotnetProject("build")), vscode.commands.registerCommand("copeland.runProject", async () => runDotnetProject("run")), vscode.commands.registerCommand("copeland.restartLanguageServer", async () => {
        const controller = controllerForActiveDocument();
        if (controller) {
            await controller.restartLanguageServer();
            updateStatus();
        }
    }), vscode.commands.registerCommand("copeland.showLanguageServerOutput", () => output.show(true)), vscode.commands.registerCommand("copeland.showProjectInfo", async () => {
        const controller = controllerForActiveDocument() ?? controllers[0];
        if (!controller) {
            vscode.window.showInformationMessage("No Copeland project was found in this workspace.");
            return;
        }
        await vscode.window.showInformationMessage(controller.describeProject(), { modal: true });
    }), vscode.commands.registerCommand("copeland.openWorkspaceManifest", async () => {
        const controller = controllerForActiveDocument() ?? controllers[0];
        if (controller) {
            const document = await vscode.workspace.openTextDocument(controller.manifestUri);
            await vscode.window.showTextDocument(document);
        }
    }), vscode.commands.registerCommand("copeland.reloadWorkspaceOwnership", async (uri) => {
        const controller = controllerForUri(uri) ?? controllerForActiveDocument();
        if (controller) {
            await controller.reloadOwnership();
            updateStatus();
        }
    }));
}
async function runDotnetProject(operation) {
    const controller = controllerForActiveDocument();
    const document = vscode.window.activeTextEditor?.document;
    if (!controller || !document) {
        return;
    }
    const owner = controller.ownerFor(document);
    const configuredProject = vscode.workspace.getConfiguration("copeland", controller.folder.uri).get("projectPath", "").trim();
    const project = configuredProject || (owner?.owner === "tscl" ? owner.project : undefined);
    if (!project) {
        vscode.window.showErrorMessage("No Copeland project is associated with the active file. Set copeland.projectPath or open a tscl-owned file.");
        return;
    }
    const absoluteProject = path.resolve(controller.rootPath, project);
    const result = await (0, toolchain_1.runTool)({ command: "dotnet", arguments: [], source: ".NET SDK" }, [operation, absoluteProject, "--disable-build-servers"], controller.rootPath, output);
    output.show(true);
    if (result.exitCode !== 0) {
        vscode.window.showErrorMessage(`Copeland project ${operation} failed. See Copeland TS Language Server output.`);
    }
}
function controllerForActiveDocument() {
    return controllerForUri(vscode.window.activeTextEditor?.document.uri);
}
function controllerForUri(uri) {
    if (!uri) {
        return undefined;
    }
    const folder = vscode.workspace.getWorkspaceFolder(uri);
    return controllers.find((controller) => controller.folder.uri.toString() === folder?.uri.toString());
}
function updateStatus() {
    const document = vscode.window.activeTextEditor?.document;
    const controller = controllerForActiveDocument();
    const text = controller?.describeActiveDocument(document);
    if (!text) {
        statusBar.hide();
        return;
    }
    statusBar.text = text;
    statusBar.tooltip = controller?.describeProject() ?? "Click to show Copeland TypeScript ownership.";
    statusBar.show();
}
async function refreshWorkspaceControllers(event) {
    for (const removed of event.removed) {
        const index = controllers.findIndex((controller) => controller.folder.uri.toString() === removed.uri.toString());
        if (index >= 0) {
            const [controller] = controllers.splice(index, 1);
            await controller.dispose();
        }
    }
    for (const added of event.added) {
        const controller = new workspaceController_1.WorkspaceController(added, output, updateStatus, extensionVersion);
        controllers.push(controller);
        await controller.initialize();
    }
    updateStatus();
}
async function disposeControllers() {
    const current = controllers;
    controllers = [];
    await Promise.all(current.map((controller) => controller.dispose()));
}
//# sourceMappingURL=extension.js.map