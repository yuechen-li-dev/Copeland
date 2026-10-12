import { defineTypeScriptWorkspace } from "copeland/workspace";

export default defineTypeScriptWorkspace({
    ownership: "partial",
    tscl: {
        project: "./Website.csproj",
        include: ["src/**"],
        types: ["ReactComponents"],
        targets: {
            browser: { backend: "javascript", runtime: "browser" }
        }
    }
});
