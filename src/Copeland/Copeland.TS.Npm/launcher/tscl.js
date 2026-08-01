#!/usr/bin/env node

"use strict";

const fs = require("node:fs");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

const packageRoot = path.resolve(__dirname, "..");
const packageManifest = JSON.parse(
    fs.readFileSync(path.join(packageRoot, "package.json"), "utf8")
);
const expectedVersion = packageManifest.version;
const toolAssembly = path.join(packageRoot, "payload", "Copeland.Cli.dll");

function fail(message) {
    process.stderr.write(`@copeland/tscl: ${message}\n`);
    process.exit(1);
}

if (process.platform !== "win32" || process.arch !== "x64") {
    fail(
        `Preview 1 supports Windows x64; detected ${process.platform}-${process.arch}. ` +
        "Use Copeland.TS.Tool directly only on a platform you have validated."
    );
}

if (!fs.existsSync(toolAssembly)) {
    fail("the packaged Copeland tool payload is missing; reinstall @copeland/tscl.");
}

const runtimeProbe = spawnSync("dotnet", ["--list-runtimes"], {
    encoding: "utf8",
    windowsHide: true
});
if (runtimeProbe.error || runtimeProbe.status !== 0) {
    fail(
        ".NET 10 was not found. Install the .NET 10 runtime or SDK from " +
        "https://dotnet.microsoft.com/download/dotnet/10.0 and reopen the terminal."
    );
}

const runtimes = runtimeProbe.stdout || "";
if (!/^Microsoft\\.NETCore\\.App 10\\./m.test(runtimes)) {
    fail(
        ".NET 10 is required, but Microsoft.NETCore.App 10.x is not installed. " +
        "Install .NET 10 and reopen the terminal."
    );
}

const versionProbe = spawnSync("dotnet", [toolAssembly, "--version"], {
    encoding: "utf8",
    windowsHide: true
});
if (versionProbe.error || versionProbe.status !== 0) {
    const detail = (versionProbe.stderr || versionProbe.error?.message || "unknown error").trim();
    fail(`the packaged Copeland tool could not start: ${detail}`);
}

const actualVersion = versionProbe.stdout.trim();
if (actualVersion !== expectedVersion) {
    fail(
        `package version ${expectedVersion} does not match tool version ${actualVersion}. ` +
        "Reinstall the matching @copeland/tscl package."
    );
}

const result = spawnSync("dotnet", [toolAssembly, ...process.argv.slice(2)], {
    stdio: "inherit",
    windowsHide: false
});
if (result.error) {
    fail(`failed to launch the packaged tool: ${result.error.message}`);
}

if (result.signal) {
    process.kill(process.pid, result.signal);
}

process.exit(result.status === null ? 1 : result.status);
