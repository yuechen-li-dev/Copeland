import { defineTypeScriptWorkspace } from "copeland/workspace";

export default defineTypeScriptWorkspace({
    ownership: "strict",
    tscl: {
        project: "./HelloCopeland.csproj",
        include: ["src/**", "tests/**"],
        types: ["TextDocuments"]
    }
});
