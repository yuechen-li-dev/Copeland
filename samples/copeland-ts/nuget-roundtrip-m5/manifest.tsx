import { Package, Targets, Workspace, define } from "tspack/manifest";

export default define(
    <Workspace name="nuget-roundtrip" runtime="nodejs">
        <Package name="nuget-roundtrip" version="1.0.0" kind="app" compiler="tscl">
            <Targets rows={[
                { name: "node", export: ".", entry: "src/Main.ts", runtime: "dist/node/main.js", deps: [] },
                { name: "clr", export: "./clr", entry: "src/Main.ts", runtime: "dist/clr/main.dll", deps: [] },
            ]} />
        </Package>
    </Workspace>,
);
