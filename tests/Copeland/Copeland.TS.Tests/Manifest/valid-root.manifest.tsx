import { CompatFiles, JsonFile, Package, RunTargets, Targets, Workspace, define, defineDeps, npm, tool } from "tspack/manifest";

const deps = defineDeps({
  typescript: tool(npm("typescript", "^5.9.0")),
});

export default define(
  <Workspace name="sample" runtime="nodejs">
    <CompatFiles>
      <JsonFile path="tsconfig.tspack.json" value={{ strict: true, include: ["manifest.tsx"] }} />
    </CompatFiles>
    <Package name="@sample/app" version="1.0.0" license="MIT" kind="app" dependencies={{ values: [deps.typescript] }}>
      <Targets rows={[{ name: "app", entry: "src/main.ts", runtime: "dist/main.js", deps: [], peers: [] }]} />
      <RunTargets rows={[{ name: "serve", runtime: "node", cwd: "package", command: ["server/main.js", "--port", "4173"] }]} />
    </Package>
  </Workspace>,
);
