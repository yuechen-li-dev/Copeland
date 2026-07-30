import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { extname, resolve, sep } from "node:path";

const root = resolve("dist/browser");
const types = { ".css": "text/css; charset=utf-8", ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".mjs": "text/javascript; charset=utf-8" };
createServer(async (request, response) => {
  const pathname = new URL(request.url ?? "/", "http://localhost").pathname;
  const file = resolve(root, pathname === "/" ? "index.html" : decodeURIComponent(pathname).replace(/^\/+/, ""));
  if (file !== root && !file.startsWith(root + sep)) return response.writeHead(403).end("Forbidden");
  try { response.writeHead(200, { "content-type": types[extname(file)] ?? "application/octet-stream" }).end(await readFile(file)); }
  catch { response.writeHead(404).end("Not found"); }
}).listen(4176, "127.0.0.1", () => console.log("Table derivation proof ready at http://127.0.0.1:4176"));
