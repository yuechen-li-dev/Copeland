import { defineTypeScriptWorkspace } from "copeland/workspace";

export default defineTypeScriptWorkspace({
    tsc: {
        include: ["src/legacy/**"],
        compilerOptions: {
            strict: true,
            target: "ES2024",
            module: "ESNext",
            moduleResolution: "bundler",
            rootDir: ".",
            outDir: "obj/tsc",
            skipLibCheck: true
        }
    },
    tscl: {
        project: "./App.csproj",
        include: ["src/copeland/**"]
    }
});
