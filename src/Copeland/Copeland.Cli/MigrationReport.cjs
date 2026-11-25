"use strict";

const fs = require("node:fs");
const path = require("node:path");
const { createRequire } = require("node:module");

function loadTypeScript(project) {
    const projectRequire = createRequire(project);
    for (const name of ["typescript", "@typescript/typescript6"]) {
        let entry;
        try {
            entry = projectRequire.resolve(name);
        } catch (error) {
            if (error.code !== "MODULE_NOT_FOUND") {
                throw error;
            }
            continue;
        }
        const compiler = projectRequire(entry);
        if (typeof compiler.getParsedCommandLineOfConfigFile === "function"
            && typeof compiler.matchFiles === "function") {
            return compiler;
        }
    }
    throw new Error("Install the project's TypeScript 5.8+ or 6 compiler API. A native TypeScript project can add @typescript/typescript6 alongside its existing compiler.");
}

function resolvedPath(file) {
    return path.resolve(file).replaceAll("\\", "/");
}

function canonicalPath(ts, file) {
    const normalized = resolvedPath(file);
    return ts.sys.useCaseSensitiveFileNames ? normalized : normalized.toLowerCase();
}

function readProjects(ts, project, host) {
    const projects = new Map();
    function visit(config) {
        const key = canonicalPath(ts, config);
        if (projects.has(key)) {
            return;
        }
        const parsed = ts.getParsedCommandLineOfConfigFile(config, {}, {
            ...host,
            onUnRecoverableConfigFileDiagnostic: diagnostic => {
                throw new Error(ts.flattenDiagnosticMessageText(diagnostic.messageText, "\n"));
            },
        });
        if (!parsed || parsed.errors.length > 0) {
            const messages = parsed?.errors.map(error => ts.flattenDiagnosticMessageText(error.messageText, "\n"));
            throw new Error(config + ": " + (messages?.join("\n") || "Could not read TypeScript configuration."));
        }
        projects.set(key, parsed);
        for (const reference of parsed.projectReferences ?? []) {
            visit(ts.resolveProjectReferencePath(reference));
        }
    }
    visit(project);
    return projects.values();
}

function previewFiles(ts, project, files) {
    const entries = new Map();
    function directoryEntries(directory) {
        const key = canonicalPath(ts, directory);
        if (!entries.has(key)) {
            entries.set(key, { files: [], directories: [] });
        }
        return entries.get(key);
    }

    for (const file of files) {
        let directory = path.dirname(file);
        directoryEntries(directory).files.push(path.basename(file));
        let parent = path.dirname(directory);
        while (directory !== parent) {
            const children = directoryEntries(parent).directories;
            const name = path.basename(directory);
            if (!children.includes(name)) {
                children.push(name);
            }
            directory = parent;
            parent = path.dirname(directory);
        }
    }

    const host = {
        ...ts.sys,
        readDirectory(root, extensions, excludes, includes, depth) {
            const actual = ts.sys.readDirectory(root, extensions, excludes, includes, depth);
            const proposed = ts.matchFiles(root, extensions, excludes, includes, ts.sys.useCaseSensitiveFileNames,
                ts.sys.getCurrentDirectory(), depth, directoryEntries, file => file);
            return [...actual, ...proposed];
        },
    };
    const included = new Set();
    for (const config of readProjects(ts, project, host)) {
        for (const file of config.fileNames) {
            included.add(canonicalPath(ts, file));
        }
    }
    return { includedFiles: files.filter(file => included.has(canonicalPath(ts, file))) };
}

function report(ts, project) {
    const root = path.dirname(project);
    const relative = file => path.relative(root, file).replaceAll("\\", "/");
    const files = new Set();
    const findings = new Map();
    for (const config of readProjects(ts, project, ts.sys)) {
        const program = ts.createProgram({
            rootNames: config.fileNames,
            options: config.options,
            projectReferences: config.projectReferences,
        });
        const checker = program.getTypeChecker();
        const owned = program.getSourceFiles()
            .filter(file => !file.isDeclarationFile && !program.isSourceFileFromExternalLibrary(file));
        const ownedPaths = new Set(owned.map(file => canonicalPath(ts, file.fileName)));
        for (const source of owned) {
            const file = relative(source.fileName);
            files.add(file);
            function add(node, code, message, edit) {
                const start = node.getStart(source);
                const length = node.end - start;
                const position = source.getLineAndCharacterOfPosition(start);
                const end = source.getLineAndCharacterOfPosition(node.end);
                findings.set(file + ":" + start + ":" + code, {
                    code, severity: "warning", file, start, length,
                    line: position.line + 1,
                    column: position.character + 1,
                    endLine: end.line + 1,
                    endColumn: end.character + 1,
                    message, edit,
                });
            }
            function visit(node, parent) {
                if (ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) {
                    const specifier = node.moduleSpecifier;
                    if (specifier && ts.isStringLiteral(specifier)) {
                        const fix = localImportEdit(ts, program, ownedPaths, source, specifier);
                        if (fix) {
                            add(specifier, "COPE-MIGRATE-0101",
                                "This relative .js specifier resolves to a TypeScript source. Use " + JSON.stringify(fix.replacement) + " when both files move into the Copeland workspace.", fix);
                        }
                    }
                    if (ts.isImportDeclaration(node) && node.importClause?.name) {
                        add(node.importClause.name, "COPE-MIGRATE-0102", "Copeland uses named source exports. Replace this default import with a named import in the migrated modules.");
                    }
                    if (ts.isExportDeclaration(node)) {
                        add(node, "COPE-MIGRATE-0103", "Copeland source modules use named declarations. Keep this re-export at the JavaScript package boundary or import the defining Copeland module directly.");
                    }
                } else if (node.kind === ts.SyntaxKind.AnyKeyword || node.kind === ts.SyntaxKind.UnknownKeyword) {
                    add(node, "COPE-MIGRATE-0104", "Choose a concrete Copeland type for this value, or validate it at the JavaScript boundary.");
                } else if (ts.isRegularExpressionLiteral(node)) {
                    add(node, "COPE-MIGRATE-0105", "Port this regular expression as a character scan or keep the matching operation in a typed JavaScript adapter.");
                } else if (ts.isHeritageClause(node) && node.token === ts.SyntaxKind.ExtendsKeyword
                    && parent && (ts.isClassDeclaration(parent) || ts.isClassExpression(parent))) {
                    add(node, "COPE-MIGRATE-0106", "Copeland classes are immutable values. Extract shared behavior into named functions and pass the state explicitly.");
                } else if (ts.isCallExpression(node) && ts.isPropertyAccessExpression(node.expression)
                    && node.expression.name.text === "push") {
                    const receiver = checker.getTypeAtLocation(node.expression.expression);
                    if (checker.isArrayType(receiver) || checker.isTupleType(receiver)) {
                        add(node.expression.name, "COPE-MIGRATE-0107", "Use a sized MutableArray and indexed writes when the final size is known. A growable buffer needs an explicit storage design.");
                    }
                } else if (ts.isStringLiteral(node) && /(?:^|[^\\])(?:\\\\)*\\[rn]/.test(node.getText(source))) {
                    add(node, "COPE-MIGRATE-0108", "Verify this escaped newline against the pinned Copeland emitter. Use a Node parity test that compares actual line-ending code units.");
                }
                ts.forEachChild(node, child => visit(child, node));
            }
            visit(source);
        }
    }

    return {
        schemaVersion: 1,
        project,
        legacyProfile: "Legacy",
        typescriptVersion: ts.version,
        files: [...files].sort(),
        findings: [...findings.values()].sort((left, right) =>
            left.file.localeCompare(right.file, "en") || left.start - right.start || left.code.localeCompare(right.code, "en")),
    };
}

function localImportEdit(ts, program, ownedPaths, source, specifier) {
    const original = specifier.text;
    if (!/^\.\.?\//.test(original) || !original.endsWith(".js")) {
        return undefined;
    }
    const resolution = program.getResolvedModuleFromModuleSpecifier(specifier, source)?.resolvedModule;
    const target = resolution?.resolvedFileName;
    if (!target || !ownedPaths.has(canonicalPath(ts, target))) {
        return undefined;
    }
    const extension = path.extname(target);
    const stem = original.slice(0, -3);
    const expected = canonicalPath(ts, path.resolve(path.dirname(source.fileName), stem + extension));
    if ((extension !== ".ts" && extension !== ".tsx") || expected !== canonicalPath(ts, target)) {
        return undefined;
    }
    const start = specifier.getStart(source) + 1;
    const length = specifier.end - start - 1;
    const needsExtension = path.basename(stem).includes(".") ||
        (extension === ".tsx" && ts.sys.fileExists(path.resolve(path.dirname(source.fileName), stem + ".ts")));
    const replacement = needsExtension ? stem + extension : stem;
    return { scope: "copelandCopy", start, length, expected: source.text.slice(start, start + length), replacement };
}

try {
    const { project, proposedFiles } = JSON.parse(fs.readFileSync(0, "utf8"));
    const ts = loadTypeScript(project);
    const result = proposedFiles ? previewFiles(ts, project, proposedFiles) : report(ts, project);
    process.stdout.write(JSON.stringify(result));
} catch (error) {
    process.stderr.write(error.message + "\n");
    process.exitCode = 1;
}
