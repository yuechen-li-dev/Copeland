import { readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { createHash } from "node:crypto";

const root = fileURLToPath(new URL("../", import.meta.url));
const evidence = fileURLToPath(new URL("../../../../docs/Copeland/reviews/evidence/vue-friction-m8/", import.meta.url));
const files = ["baseline/IssuesPage.vue", "src/Application.ts", "src/IssuesPage.ts", "src/State.ts", "src/UI.tsx", "src/Layouts.ts", "src/Lifecycle.tsx"];
const results = [];
for (const file of files) {
    const source = await readFile(root + file, "utf8");
    results.push({
        file,
        nonblankLines: source.split(/\r?\n/).filter(line => line.trim().length > 0).length,
        sha256: createHash("sha256").update(source).digest("hex"),
    });
}
await writeFile(evidence + "authoring-accounting.json", JSON.stringify({
    note: "Counts describe different ownership boundaries. UI projection and reusable data contracts count as supporting source, not free application code.",
    results,
}, null, 2) + "\n");
