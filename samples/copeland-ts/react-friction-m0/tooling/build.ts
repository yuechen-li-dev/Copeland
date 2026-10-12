import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { copyFile, mkdir } from "node:fs/promises";

const require = createRequire(new URL("../baseline/package.json", import.meta.url));
const { build } = require("esbuild") as typeof import("../baseline/node_modules/esbuild/lib/main.js");
const root = fileURLToPath(new URL("../", import.meta.url));

// Materialize the existing host source for TSPack's workspace-local path
// dependency policy. The fixture does not maintain a second host implementation.
await mkdir(`${root}/runtime/dist`, { recursive: true });
await copyFile(`${root}/../copeland-website-m0/runtime/browser-v1.js`, `${root}/runtime/dist/browser-v1.js`);

await build({
    absWorkingDir: root,
    entryPoints: ["baseline/App.tsx"],
    outfile: "dist/baseline/app.js",
    bundle: true,
    format: "esm",
    jsx: "automatic",
    external: ["react", "react-dom/client", "react/jsx-runtime", "@base-ui-components/react/dialog"],
    sourcemap: true
});

