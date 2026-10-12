import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { extname, resolve, sep } from "node:path";

const root = fileURLToPath(new URL("../", import.meta.url));
const browserRoot = resolve(root, "dist/browser");
const baselineRoot = resolve(root, "dist/baseline");
const types: Record<string, string> = {
    ".html": "text/html", ".css": "text/css", ".js": "text/javascript",
    ".mjs": "text/javascript", ".json": "application/json", ".map": "application/json"
};

export async function startServer(port = 0, artifactRoot = browserRoot, extraAssets: Record<string, string> = {}) {
    const normalizedArtifactRoot = resolve(artifactRoot);
    const server = createServer(async (request, response) => {
        try {
            const path = new URL(request.url ?? "/", "http://127.0.0.1").pathname;
            if (path === "/" || path === "/baseline/") {
                let html = await readFile(resolve(normalizedArtifactRoot, "index.html"), "utf8");
                if (path === "/baseline/") {
                    html = html.replace(/<script type="module" src="\.\/main\.js"><\/script>/,
                        '<script type="module" src="/baseline/app.js"></script>');
                    html = html.replace(/<script type="module" src="\.\/(component-frame-loader|attachment-plan-loader)\.js"><\/script>\s*/g, "");
                }
                // Both pages use exactly the TSPack-materialized React singleton
                // and transformed Base UI modules, under one asset base.
                html = html.replace("<head>", '<head><base href="/">');
                response.writeHead(200, { "Content-Type": "text/html" });
                response.end(html);
                return;
            }
            if (extraAssets[path]) {
                const content = await readFile(extraAssets[path]);
                response.writeHead(200, { "Content-Type": types[extname(extraAssets[path])] ?? "application/octet-stream" });
                response.end(content);
                return;
            }
            if (path === "/favicon.ico") {
                response.writeHead(204);
                response.end();
                return;
            }
            const base = path.startsWith("/baseline/") ? baselineRoot : normalizedArtifactRoot;
            const relative = path.startsWith("/baseline/") ? path.slice(10) : path.slice(1);
            const target = resolve(base, relative);
            if (!target.startsWith(base + sep)) {
                response.writeHead(403);
                response.end();
                return;
            }
            const content = await readFile(target);
            response.writeHead(200, { "Content-Type": types[extname(target)] ?? "application/octet-stream" });
            response.end(content);
        } catch {
            response.writeHead(404);
            response.end("Missing generated asset");
        }
    });
    await new Promise<void>(ready => server.listen(port, "127.0.0.1", ready));
    const address = server.address();
    if (!address || typeof address === "string") {
        throw new Error("Expected a TCP server address.");
    }
    return {
        url: `http://127.0.0.1:${address.port}`,
        close: () => new Promise<void>((done, reject) => server.close(error => error ? reject(error) : done()))
    };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
    const server = await startServer(Number(process.env.PORT ?? "4178"));
    process.stdout.write(`Copeland: ${server.url}/\nOrdinary TS: ${server.url}/baseline/\n`);
    for (const signal of ["SIGINT", "SIGTERM"] as const) {
        process.once(signal, async () => {
            await server.close();
            process.exit(0);
        });
    }
}
