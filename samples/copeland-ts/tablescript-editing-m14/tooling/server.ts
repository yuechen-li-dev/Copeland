import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { extname, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

export async function startServer() {
    const root = fileURLToPath(new URL("../dist/", import.meta.url));
    const mime: Record<string, string> = { ".html": "text/html", ".css": "text/css", ".js": "text/javascript", ".map": "application/json" };
    const server = createServer(async (request, response) => {
        try {
            const pathname = new URL(request.url ?? "/", "http://127.0.0.1").pathname;
            if (pathname === "/favicon.ico") {
                response.writeHead(204).end();
                return;
            }
            const target = resolve(root, "." + pathname);
            if (!target.startsWith(resolve(root) + sep)) {
                response.writeHead(403).end();
                return;
            }
            const content = await readFile(target);
            response.writeHead(200, { "Content-Type": mime[extname(target)] ?? "application/octet-stream" });
            response.end(content);
        } catch {
            response.writeHead(404).end("Missing artifact");
        }
    });
    await new Promise<void>(ready => server.listen(0, "127.0.0.1", ready));
    const address = server.address();
    if (address === null || typeof address === "string") throw new Error("Missing loopback server address");
    return {
        url: `http://127.0.0.1:${address.port}`,
        close: () => new Promise<void>((done, reject) => server.close(error => error ? reject(error) : done())),
    };
}
