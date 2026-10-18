import { createServer } from "node:http";
import { readFile, realpath } from "node:fs/promises";
import { extname, isAbsolute, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

export async function startServer(directory: URL, port = 0) {
    const root = await realpath(fileURLToPath(directory));
    const mime: Record<string, string> = {
        ".html": "text/html",
        ".css": "text/css",
        ".js": "text/javascript",
        ".map": "application/json",
    };
    function isOutside(target: string): boolean {
        const path = relative(root, target);
        return path === ".." || path.startsWith(`..${sep}`) || isAbsolute(path);
    }
    const server = createServer(async (request, response) => {
        try {
            const pathname = decodeURIComponent(new URL(request.url ?? "/", "http://127.0.0.1").pathname);
            if (pathname === "/favicon.ico") {
                response.writeHead(204).end();
                return;
            }
            let target = resolve(root, "." + pathname);
            if (isOutside(target)) {
                response.writeHead(403).end();
                return;
            }
            target = await realpath(target);
            if (isOutside(target)) {
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
    await new Promise<void>((ready, reject) => {
        server.once("error", reject);
        server.listen(port, "127.0.0.1", ready);
    });
    const address = server.address();
    if (address === null || typeof address === "string") {
        throw new Error("Missing loopback server address");
    }
    return {
        url: `http://127.0.0.1:${address.port}`,
        close: () => new Promise<void>((done, reject) => {
            server.close(error => {
                if (error) {
                    reject(error);
                } else {
                    done();
                }
            });
        }),
    };
}
