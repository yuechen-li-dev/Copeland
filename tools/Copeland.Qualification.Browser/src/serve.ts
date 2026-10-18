import { resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { parseArgs } from "node:util";
import { startServer } from "./server.ts";

const { values } = parseArgs({
    options: {
        root: { type: "string", default: "dist" },
        port: { type: "string", default: "4191" },
    },
});
const port = Number(values.port);
if (!Number.isInteger(port) || port < 0 || port > 65535) {
    throw new Error("--port must be an integer between 0 and 65535.");
}
const server = await startServer(pathToFileURL(resolve(values.root)), port);
process.stdout.write(`Browser example: ${server.url}/diagnostic/index.html\nStop with Ctrl+C.\n`);
for (const signal of ["SIGINT", "SIGTERM"] as const) {
    process.once(signal, async () => {
        await server.close();
    });
}
