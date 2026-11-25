import { Package, Targets, Workspace, define } from "tspack/manifest";

export default define(
    <Workspace name="nuget-worker" runtime="nodejs">
        <Package name="nuget-worker" version="1.0.0" kind="app">
            <Targets rows={[{
                name: "node",
                export: ".",
                entry: "src/Main.ts",
                runtime: "dist/main.js",
                deps: [],
            }]} />
        </Package>
    </Workspace>,
);
