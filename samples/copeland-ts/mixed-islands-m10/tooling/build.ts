import { mkdir, copyFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { build } from "esbuild";

const root = fileURLToPath(new URL("../", import.meta.url));
await mkdir(root + "dist", { recursive: true });
await copyFile(root + "styles.css", root + "dist/styles.css");
for (const adapter of ["react", "vue"]) {
    for (const profile of ["diagnostic", "symbolic", "production"]) {
        const destination = `${root}dist/${adapter}/${profile}`;
        await build({
            absWorkingDir: root,
            entryPoints: [`tooling/${adapter}-entry.ts`],
            outfile: destination + "/main.js",
            bundle: true,
            format: "esm",
            nodePaths: [root + "node_modules"],
            define: {
                __VUE_OPTIONS_API__: "true",
                __VUE_PROD_DEVTOOLS__: "false",
                __VUE_PROD_HYDRATION_MISMATCH_DETAILS__: "false",
            },
            alias: {
                "@demo": `${root}artifacts/${adapter}/${profile}/src/Application.js`,
                "@copeland/island-controls": `${root}tooling/${adapter}-hosts.ts`,
                "@copeland/lifecycle-host": `${root}../react-friction-m0/tooling/lifecycle-host.ts`,
                "@copeland/browser-v1": `${root}../react-friction-m0/dist/browser/packages/copeland-browser-v1/index.js`,
            },
        });
        await copyFile(
            `${root}artifacts/${adapter}/${profile}/generated/layouts.css`,
            destination + "/layouts.css",
        );
        await writeFile(
            destination + "/index.html",
            '<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Copeland mixed islands</title><link rel="stylesheet" href="/styles.css"><link rel="stylesheet" href="./layouts.css"></head><body><div id="app" class="shell"></div><script type="module" src="./main.js"></script></body></html>',
        );
    }
}
