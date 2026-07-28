import { define, dep, npm, path, Policies } from "tspack/manifest";

const deps = {
  react: dep(npm("react", "19.2.7")),
  reactDom: dep(npm("react-dom", "19.2.7")),
  browserHost: dep(path("runtime"), { key: "@copeland/browser-v1" }),
};

export default define(
  <Workspace name="copeland-standalone-web-m0" runtime="nodejs">
    <Package
      name="copeland-standalone-web-m0"
      version="1.0.0"
      kind="app"
      compiler="tscl"
      compilerPath=".copeland/compiler/Copeland.Cli.exe"
      dependencies={{ values: [deps.react, deps.reactDom, deps.browserHost] }}
    >
      <Policies types={{ missingTypes: "ignore" }} />
      <Targets rows={[{
        name: "browser-materialization",
        export: ".",
        entry: "src/Materialization.ts",
        runtime: "generated/tspack-materialization.js",
        types: "generated/tspack-materialization.d.ts",
        javascriptRuntime: "browser",
        deps: [deps.react, deps.reactDom, deps.browserHost],
        npmContracts: [
          { package: "react", exports: [] },
          { package: "react-dom/client", exports: [] },
        ],
      }]} />
      <Publish include={[]} />
    </Package>
  </Workspace>,
);
