import { readFile, mkdir, writeFile, copyFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { build, type Plugin } from "esbuild";
import { parse, compileScript, compileStyle } from "@vue/compiler-sfc";
import { createHash } from "node:crypto";
import { dirname, relative, resolve } from "node:path";

const root = fileURLToPath(new URL("../", import.meta.url));
const vueFlags = {
    __VUE_OPTIONS_API__: "true",
    __VUE_PROD_DEVTOOLS__: "false",
    __VUE_PROD_HYDRATION_MISMATCH_DETAILS__: "false",
};
const sfc: Plugin = {
    name: "vue-sfc",
    setup(builder) {
        builder.onLoad({ filter: /\.vue$/ }, async args => {
            const source = await readFile(args.path, "utf8");
            const parsed = parse(source, { filename: args.path });
            if (parsed.errors.length > 0) throw new Error(String(parsed.errors[0]));
            const id = createHash("sha256").update(args.path.slice(root.length)).digest("hex").slice(0, 8);
            const scopeId = `data-v-${id}`;
            const script = compileScript(parsed.descriptor, { id, inlineTemplate: true, genDefaultAs: "__component" });
            const styles: string[] = [];
            for (const style of parsed.descriptor.styles) {
                const compiled = compileStyle({ source: style.content, filename: args.path, id: scopeId, scoped: style.scoped });
                if (compiled.errors.length > 0) throw new Error(String(compiled.errors[0]));
                styles.push(compiled.code);
            }
            const css = resolve(root, "artifacts/sfc", relative(root, args.path) + ".css");
            await mkdir(dirname(css), { recursive: true });
            await writeFile(css, styles.join("\n"));
            const scoped = parsed.descriptor.styles.some(style => style.scoped);
            const tail = scoped ? `\n__component.__scopeId = ${JSON.stringify(scopeId)};` : "";
            return {
                contents: script.content + tail + `\nimport ${JSON.stringify(css)};\nexport default __component;`,
                loader: "ts",
                resolveDir: dirname(args.path),
            };
        });
    },
};
await mkdir(`${root}dist`, { recursive: true });
await copyFile(`${root}../react-friction-m0/styles.css`, `${root}dist/styles.css`);
const profiles = ["diagnostic", "symbolic", "production"];
for (const profile of profiles) {
    for (const demo of ["Application", "Lifecycle"]) {
        await build({
            absWorkingDir: root,
            entryPoints: ["tooling/entry.ts"],
            outfile: `dist/${profile}/${demo.toLowerCase()}/main.js`,
            bundle: true,
            format: "esm",
            define: vueFlags,
            alias: {
                "@demo": `${root}artifacts/${profile}/src/${demo}.js`,
                "@copeland/vue-controls": `${root}tooling/controls.ts`,
                "@copeland/lifecycle-host": `${root}../react-friction-m0/tooling/lifecycle-host.ts`,
                "@copeland/browser-v1": `${root}../react-friction-m0/dist/browser/packages/copeland-browser-v1/index.js`,
            },
            plugins: [sfc],
        });
        const layout = await readFile(`${root}artifacts/${profile}/generated/layouts.css`, "utf8");
        await writeFile(`${root}dist/${profile}/${demo.toLowerCase()}/layouts.css`, layout);
    }
}
await build({
    absWorkingDir: root,
    entryPoints: ["baseline/Main.ts"],
    outfile: "dist/baseline/main.js",
    bundle: true,
    format: "esm",
    define: vueFlags,
    plugins: [sfc],
});
const html = (css: string) => `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Copeland Vue friction</title><link rel="stylesheet" href="/styles.css"><link rel="stylesheet" href="./main.css">${css}</head><body><main id="app" class="shell"></main><script type="module" src="./main.js"></script></body></html>`;
for (const profile of profiles) {
    for (const demo of ["application", "lifecycle"]) {
        await writeFile(`${root}dist/${profile}/${demo}/index.html`, html('<link rel="stylesheet" href="./layouts.css">'));
    }
}
await writeFile(`${root}dist/baseline/index.html`, html(""));
