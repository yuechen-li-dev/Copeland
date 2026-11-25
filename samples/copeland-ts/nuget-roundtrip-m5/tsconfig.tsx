import { defineTypeScriptWorkspace } from "copeland/workspace";

export default defineTypeScriptWorkspace({
    ownership: "partial",
    tscl: {
        project: "./App.csproj",
        include: ["src/**"],
        targets: {
            node: { backend: "javascript", runtime: "node" },
            clr: { backend: "csharp", runtime: "ryujit" },
        },
    },
});
