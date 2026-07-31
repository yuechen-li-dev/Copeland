import { defineTypeScriptWorkspace } from "copeland/workspace";

export default defineTypeScriptWorkspace({
    ownership: "strict",
    tsc: {
        include: ["src/traditional/**"],
        compilerOptions: {
            strict: true,
            target: "ES2024",
            module: "ESNext"
        }
    },
    tscl: {
        project: "./CopelandHello.csproj",
        include: ["src/copeland/**"],
        types: ["TextDocuments"]
    }
});
