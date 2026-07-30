import { define, dep, npm, path, Policies, RunTargets } from "tspack/manifest";

const deps = {
  react: dep(npm("react", "19.2.7")),
  reactDom: dep(npm("react-dom", "19.2.7")),
  browserHost: dep(path("runtime"), { key: "@copeland/browser-v1" }),
};

export default define(
  <Workspace name="copeland-layout-table-browser-proof" runtime="nodejs">
    <Package name="copeland-layout-table-browser-proof" version="1.0.0" kind="app" root="."
      compiler="tscl" compilerPath="../../../../src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.exe"
      dependencies={{ values: [deps.react, deps.reactDom, deps.browserHost] }}>
      <Policies types={{ missingTypes: "ignore" }} />
      <RunTargets rows={[{ name: "site", runtime: "system", cwd: "workspace", command: ["node", "server.mjs"], url: "http://127.0.0.1:4175", ready: { kind: "http", path: "/" } }]} />
      <Targets rows={[{ name: "browser", export: ".", entry: "src/Main.tsx", runtime: "dist/browser/main.js", types: "dist/browser/main.d.ts", javascriptRuntime: "browser", tsXmlProfile: "react-m0", deps: [deps.react, deps.reactDom, deps.browserHost], npmContracts: [
        { package: "react", exports: [{ name: "createElement", parameters: [], result: "ReactNode" }] },
        { package: "react-dom/client", exports: [{ name: "createRoot", parameters: ["ReactMountElement"], result: "ReactRoot" }] },
        { package: "react/jsx-runtime", exports: [] },
      ] }]} />
      <Publish include={[]} />
    </Package>
  </Workspace>,
);
