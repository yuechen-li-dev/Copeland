import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { extname, resolve, sep } from "node:path";

const root = fileURLToPath(new URL("../dist/", import.meta.url));
const contentTypes: Record<string, string> = {
    ".html": "text/html",
    ".js": "text/javascript",
    ".css": "text/css",
    ".map": "application/json",
};

export async function startServer(port = 0) {
    const server = createServer(async (request, response) => {
        const url = new URL(request.url ?? "/", "http://127.0.0.1");
        if (url.pathname.startsWith("/lifecycle-api/")) {
            let value = url.searchParams.get("query") ?? "all";
            if (url.pathname.endsWith("/save"))
                value = url.searchParams.get("draft") ?? "";
            response.writeHead(200, { "Content-Type": "application/json" });
            response.end(JSON.stringify({ ok: true, value }));
            return;
        }
        if (url.pathname === "/favicon.ico") {
            response.writeHead(204);
            response.end();
            return;
        }
        let path = url.pathname;
        if (path === "/") path = "/react/diagnostic/application/";
        if (path.endsWith("/")) path += "index.html";
        const target = resolve(root, "." + path);
        if (!target.startsWith(resolve(root) + sep)) {
            response.writeHead(403);
            response.end();
            return;
        }
        try {
            const content = await readFile(target);
            response.writeHead(200, {
                "Content-Type":
                    contentTypes[extname(target)] ?? "application/octet-stream",
            });
            response.end(content);
        } catch {
            response.writeHead(404);
            response.end("Missing generated asset");
        }
    });
    await new Promise<void>((done) => server.listen(port, "127.0.0.1", done));
    const address = server.address();
    if (!address || typeof address === "string")
        throw new Error("Expected TCP server address");
    return {
        url: `http://127.0.0.1:${address.port}`,
        close: () =>
            new Promise<void>((done, reject) =>
                server.close((error) => {
                    if (error) reject(error);
                    else done();
                }),
            ),
    };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
    const server = await startServer(Number(process.env.PORT ?? "4189"));
    process.stdout.write(
        `Copeland React: ${server.url}/react/diagnostic/application/\nCopeland Vue: ${server.url}/vue/diagnostic/application/\nVue SFC: ${server.url}/baseline/\nLifecycle: ${server.url}/react/diagnostic/lifecycle/\n`,
    );
    for (const signal of ["SIGINT", "SIGTERM"] as const) {
        process.once(signal, async () => {
            await server.close();
            process.exit(0);
        });
    }
}
