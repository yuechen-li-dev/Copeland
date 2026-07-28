import { strict as assert } from "assert";
import { OwnershipMap } from "./ownershipMap";

suite("OwnershipMap", () => {
    test("routes only metadata-listed tscl files without examining source syntax", () => {
        const map = new OwnershipMap({
            schemaVersion: 1,
            workspaceRoot: ".",
            files: [
                { path: "src/copeland/Domain.ts", owner: "tscl", project: "App.csproj", matchedRule: "src/copeland/**" },
                { path: "src/legacy/Legacy.ts", owner: "tsc", project: "generated", matchedRule: "src/legacy/**" }
            ]
        });

        assert.equal(map.get("src/copeland/Domain.ts")?.owner, "tscl");
        assert.equal(map.get("src/legacy/Legacy.ts")?.owner, "tsc");
        assert.equal(map.get("src/copeland/Unlisted.ts"), undefined);
    });

    test("rejects unsupported metadata instead of falling back to globs", () => {
        assert.throws(() => new OwnershipMap({ schemaVersion: 2, workspaceRoot: ".", files: [] }));
    });
});
