import { Package, Targets, Workspace, define } from "tspack/manifest";

export default define(
    <Workspace name="nuget-browser" runtime="nodejs">
        <Package name="nuget-browser" version="1.0.0" kind="app" compiler="tscl">
            <Targets rows={[
                { name: "node", export: ".", entry: "src/Main.ts", runtime: "dist/node/main.js", deps: [] },
                { name: "clr", export: "./clr", entry: "src/Main.ts", runtime: "dist/clr/main.dll", deps: [] },
                { name: "browser-wasm", export: "./browser-wasm", entry: "src/Main.ts", runtime: "dist/wasm/main.js", deps: [] },
                { name: "browser-service", export: "./browser-service", entry: "src/Main.ts", runtime: "dist/service/main.js", deps: [] },
            ]} />
        </Package>
    </Workspace>,
);
