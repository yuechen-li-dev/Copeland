import { buildBrowserProfiles } from "../../../../tools/Copeland.Qualification.Browser/src/build.ts";

await buildBrowserProfiles(new URL("../", import.meta.url), {
    "@cafe": "Cafe.js",
    "@application": "Application.js",
    "@editor": "Rows.js",
    "@row-editor": "RowEditor.js"
});
