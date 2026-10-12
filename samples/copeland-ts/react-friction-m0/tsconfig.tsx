import { defineTypeScriptWorkspace } from "copeland/workspace";

export default defineTypeScriptWorkspace({
    ownership: "partial",
    tscl: {
        project: "./Friction.csproj",
        include: ["src/**"],
        types: ["ReactComponents"],
        targets: {
            browser: { backend: "javascript", runtime: "browser" }
        }
    }
});
