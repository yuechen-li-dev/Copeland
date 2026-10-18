import { build } from "esbuild";
import { copyFile, mkdir } from "node:fs/promises";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

// Bundling is tooling. TSPack remains the browser frame/lifecycle runtime owner.
export async function buildBrowserProfiles(directory: URL, modules: Record<string, string>, hostModules: Record<string, string> = {}) {
    const root = fileURLToPath(directory);
    for (const profile of ["diagnostic", "symbolic", "production"]) {
        const output = resolve(root, "dist", profile);
        const alias: Record<string, string> = {
            "@copeland/browser-v1": resolve(root, "../react-friction-m0/dist/browser/packages/copeland-browser-v1/index.js"),
        };
        for (const [name, source] of Object.entries(modules)) {
            alias[name] = resolve(root, "artifacts", profile, "src", source);
        }
        for (const [name, source] of Object.entries(hostModules)) {
            alias[name] = resolve(root, source);
        }
        await mkdir(output, { recursive: true });
        await build({
            absWorkingDir: root,
            entryPoints: ["tooling/entry.ts"],
            outfile: resolve(output, "main.js"),
            bundle: true,
            format: "esm",
            sourcemap: true,
            alias,
        });
        await copyFile(resolve(root, "artifacts", profile, "generated/layouts.css"), resolve(output, "layouts.css"));
        await copyFile(resolve(root, "styles.css"), resolve(output, "styles.css"));
        await copyFile(resolve(root, "index.html"), resolve(output, "index.html"));
    }
}
