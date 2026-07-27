import { define, dep, npm, path, Policies } from "tspack/manifest";

const deps = {
  react: dep(npm("react", "19.2.7")),
  reactDom: dep(npm("react-dom", "19.2.7")),
  baseUi: dep(npm("@base-ui-components/react", "1.0.0-rc.0")),
  browserHost: dep(path("runtime"), { key: "@copeland/browser-v1" }),
};

export default define(
  <Workspace name="copeland-react-components-m1" runtime="nodejs">
    <Package
      name="copeland-react-components-m1"
      version="1.0.0"
      kind="app"
      compiler="tscl"
      compilerPath="../../../src/Copeland/Copeland.Cli/bin/Debug/net10.0/Copeland.Cli.exe"
      dependencies={{ values: [deps.react, deps.reactDom, deps.baseUi, deps.browserHost] }}
    >
      <Policies types={{ missingTypes: "ignore" }} />
      <Targets rows={[{
        name: "browser",
        export: ".",
        entry: "src/Main.ts",
        runtime: "dist/browser/main.js",
        types: "dist/browser/main.d.ts",
        javascriptRuntime: "browser",
        tsXmlProfile: "react-m0",
        deps: [deps.react, deps.reactDom, deps.baseUi, deps.browserHost],
        npmContracts: [
          { package: "react", exports: [{ name: "createElement", parameters: [], result: "ReactNode" }] },
          { package: "react-dom/client", exports: [{ name: "createRoot", parameters: ["ReactMountElement"], result: "ReactRoot" }] },
          {
            package: "react/jsx-runtime",
            exports: [],
          },
          {
            package: "@base-ui-components/react/dialog",
            exports: [],
            components: [{
              name: "Dialog",
              members: [
                { name: "Root", properties: [
                  { name: "open", type: "boolean" },
                  { name: "onOpenChange", type: "(boolean)=>void" },
                  { name: "children", type: "ReactNode" },
                ] },
                { name: "Portal", properties: [{ name: "children", type: "ReactNode" }] },
                { name: "Backdrop", properties: [{ name: "className", type: "string" }] },
                { name: "Popup", properties: [
                  { name: "className", type: "string" },
                  { name: "children", type: "ReactNode" },
                ] },
                { name: "Title", properties: [{ name: "children", type: "ReactNode" }] },
                { name: "Description", properties: [{ name: "children", type: "ReactNode" }] },
                { name: "Close", properties: [{ name: "children", type: "ReactNode" }] },
              ],
            }],
          },
        ],
      }]} />
      <Publish include={[]} />
    </Package>
  </Workspace>,
);
