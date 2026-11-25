import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { cp, mkdir, mkdtemp, readFile, readdir, writeFile } from "node:fs/promises";
import { delimiter, dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { parseArgs } from "node:util";

const { values } = parseArgs({
    options: {
        feed: { type: "string" },
        out: { type: "string" },
        version: { type: "string", default: "0.1.0-preview.1" },
        "tspack-version": { type: "string", default: "v0.1.14-m80i.2" },
    },
});
assert.ok(values.feed && values.out, "Usage: node tools/Test-CopelandPackageInterop.mjs --feed <directory> --out <directory>");
const repository = fileURLToPath(new URL("../", import.meta.url));
const feed = resolve(values.feed);
const output = resolve(values.out);
const version = values.version;
const windows = process.platform === "win32";
const sdkList = run("dotnet", ["--list-sdks"], repository);
const sdk = sdkList.split(/\r?\n/)
    .map(line => /^(10\.\d+\.\d+)\s+\[/.exec(line)?.[1])
    .filter(Boolean).sort((a, b) => a.localeCompare(b, "en", { numeric: true })).at(-1);
assert.ok(sdk, "Install a stable .NET 10 SDK for package qualification.");

await mkdir(output, { recursive: true });
const root = await mkdtemp(join(output, "interop-"));
const tools = join(root, "tools");
const freshEnvironment = {
    ...process.env,
    PATH: tools + delimiter + process.env.PATH,
    DOTNET_CLI_HOME: join(root, "dotnet-home"),
    DOTNET_NOLOGO: "1",
    MSBUILDDISABLENODEREUSE: "1",
    NUGET_PACKAGES: join(root, "nuget-cache"),
    npm_config_cache: join(root, "npm-cache"),
};
const tscl = join(tools, windows ? "tscl.exe" : "tscl");
const config = join(root, "NuGet.Config");
await writeFile(join(root, "global.json"), JSON.stringify({ sdk: { version: sdk, rollForward: "disable" } }));
for (const file of ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"]) {
    await writeFile(join(root, file), "<Project />\n");
}
await writeFile(config, `<configuration>
    <packageSources>
        <clear />
        <add key="candidate" value="${escapeXml(feed)}" />
        <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    </packageSources>
    <packageSourceMapping>
        <packageSource key="candidate"><package pattern="Copeland.TS.*" /></packageSource>
        <packageSource key="nuget.org"><package pattern="*" /></packageSource>
    </packageSourceMapping>
</configuration>
`);
run("dotnet", ["tool", "install", "Copeland.TS.Tool", "--version", version, "--tool-path", tools, "--configfile", config], root, freshEnvironment);
assert.equal(run(tscl, ["--version"], root, freshEnvironment).trim(), version);
const tspackVersion = run("tspack", ["--version"], root, freshEnvironment).split(/\r?\n/)[0];
assert.equal(tspackVersion.split(" ")[1]?.split("+")[0], values["tspack-version"]);
assert.ok(!tspackVersion.includes("modified"), "Use the unmodified TSPack release binary.");

const samples = join(repository, "samples", "copeland-ts");
const npmProject = join(root, "npm-consumer");
await copyProject(join(samples, "CopelandHello", "CopelandHello"), npmProject,
    ["CopelandHello.csproj", "Program.cs", "package.json", "package-lock.json", "tsconfig.tsx", "contracts", "src"]);
runNpm(["ci"], npmProject);
const npmPublish = join(root, "npm-publish");
run("dotnet", ["publish", "CopelandHello.csproj", "-c", "Release", "-o", npmPublish], npmProject, freshEnvironment);
const deploy = join(root, "deployed");
const outside = join(root, "outside");
await mkdir(outside);
const npmDeployment = join(deploy, "npm");
await cp(npmPublish, npmDeployment, { recursive: true });
const npmResult = run("dotnet", [join(npmDeployment, "CopelandHello.dll")], outside, freshEnvironment);
assert.ok(npmResult.split(/\r?\n/).includes("lodash-es says: helloFromNpm"), npmResult);
assert.ok(npmResult.split(/\r?\n/).includes('"helloFromNpm"'), npmResult);

const roundtrip = join(root, "roundtrip");
await copyProject(join(samples, "nuget-roundtrip-m5"), roundtrip,
    ["App.csproj", "manifest.tsx", "tsconfig.tsx", "nuget-worker.json", "src"]);
const manifest = join(roundtrip, "manifest.tsx");
const authoredManifest = await readFile(manifest, "utf8");
const compilerPath = JSON.stringify(tscl.replaceAll("\\", "/"));
await writeFile(manifest, authoredManifest.replace('compiler="tscl"', 'compiler="tscl" compilerPath=' + compilerPath));
run(tscl, ["nuget", "generate", "."], roundtrip, freshEnvironment);
run(tscl, ["check", "--standalone", "."], roundtrip, freshEnvironment);
run("tspack", ["update"], roundtrip, freshEnvironment);
run("tspack", ["build"], roundtrip, freshEnvironment);
const nodeDeployment = join(deploy, "node");
const clrDeployment = join(deploy, "clr");
await cp(join(roundtrip, "dist", "node"), nodeDeployment, { recursive: true });
await cp(join(roundtrip, "dist", "clr"), clrDeployment, { recursive: true });
const nodeResult = run(process.execPath, [join(nodeDeployment, "main.js")], outside, freshEnvironment).trim();
const clrResult = run("dotnet", [join(clrDeployment, "main.dll")], outside, freshEnvironment).trim();
assert.equal(nodeResult, clrResult);
const shipment = JSON.parse(nodeResult);
assert.equal(shipment.name, 'Ship "the docs" 🌍');
assert.deepEqual(shipment.address, { city: "Montréal", unit: null, floor: 0 });
assert.deepEqual(shipment.tags, [null, ""]);
const files = await readdir(deploy, { recursive: true });
assert.ok(files.some(file => file.endsWith(join("worker", "Newtonsoft.Json.dll"))));
assert.ok(!files.some(file => /Microsoft\.CodeAnalysis/i.test(file)), "Publish contains a compiler dependency.");

const evidence = {
    platform: process.platform, sdk, copeland: version, tspack: tspackVersion,
    npmOutput: npmResult.trim(), newtonsoftOutput: shipment,
    roundtrip, tscl, nugetPackages: freshEnvironment.NUGET_PACKAGES,
};
await writeFile(join(output, "interop.json"), JSON.stringify(evidence, null, 2) + "\n");
console.log(`Package interop qualified in ${root}`);

function run(command, args, cwd, env = process.env) {
    console.log(`> ${command} ${args.join(" ")}`);
    const result = spawnSync(command, args, { cwd, env, encoding: "utf8", windowsHide: true, timeout: 180000 });
    if (result.error || result.status !== 0) {
        throw new Error([result.error?.message, result.stdout, result.stderr].filter(Boolean).join("\n"));
    }
    if (result.stderr) process.stderr.write(result.stderr);
    return result.stdout;
}

function runNpm(args, cwd) {
    if (windows) {
        const npm = join(dirname(process.execPath), "node_modules", "npm", "bin", "npm-cli.js");
        run(process.execPath, [npm, ...args], cwd, freshEnvironment);
    } else {
        run("npm", args, cwd, freshEnvironment);
    }
}

async function copyProject(source, destination, entries) {
    await mkdir(destination);
    for (const entry of entries) {
        await cp(join(source, entry), join(destination, entry), { recursive: true });
    }
}

function escapeXml(text) {
    return text.replaceAll("&", "&amp;").replaceAll('"', "&quot;").replaceAll("<", "&lt;");
}
