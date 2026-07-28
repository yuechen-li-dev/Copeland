import { defineTypeScriptWorkspace } from "copeland/workspace";

export default defineTypeScriptWorkspace({
    // TSPack's manifest.tsx is intentionally outside the Copeland/tsc ownership map.
    ownership: "partial",
    tscl: {
        project: "./Copeland.React.csproj",
        include: ["Copeland/**"]
    }
});
