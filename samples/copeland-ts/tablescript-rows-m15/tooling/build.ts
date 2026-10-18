import { build } from "esbuild";
import { copyFile, mkdir, readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../", import.meta.url));
for (const profile of ["diagnostic", "symbolic", "production"]) {
    const output = `${root}dist/${profile}`;
    await mkdir(output, { recursive: true });
    await build({
        absWorkingDir: root,
        entryPoints: ["tooling/entry.ts"],
        outfile: `${output}/main.js`,
        bundle: true,
        format: "esm",
        sourcemap: true,
        alias: {
            "@cafe": `${root}artifacts/${profile}/src/Cafe.js`,
            "@application": `${root}artifacts/${profile}/src/Application.js`,
            "@editor": `${root}artifacts/${profile}/src/Rows.js`,
            "@row-editor": `${root}artifacts/${profile}/src/RowEditor.js`,
            "@copeland/cafe-controls": `${root}tooling/controls.ts`,
            "@copeland/browser-v1": `${root}../react-friction-m0/dist/browser/packages/copeland-browser-v1/index.js`,
        },
    });
    await copyFile(`${root}artifacts/${profile}/generated/layouts.css`, `${output}/layouts.css`);
    await copyFile(`${root}styles.css`, `${output}/styles.css`);
    const html = await readFile(`${root}index.html`, "utf8");
    await writeFile(`${output}/index.html`, html);
}
