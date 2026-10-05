import { defineTypeScriptWorkspace } from "copeland/workspace";

export default defineTypeScriptWorkspace({
    ownership: "strict",
    tsc: {
        include: ["src/legacy/**"],
        compilerOptions: { strict: true, target: "ES2024", module: "ESNext" }
    },
    tscl: {
        project: "./Copeland.Workspace.csproj",
        include: ["src/copeland/**"]
    }
});
