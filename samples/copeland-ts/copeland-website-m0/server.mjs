import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { extname, resolve, sep } from "node:path";

const host = "127.0.0.1";
const port = 4173;
const assetRoot = resolve("dist/browser");
const contentTypes = {
  ".css": "text/css; charset=utf-8",
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".mjs": "text/javascript; charset=utf-8",
};

function assetPath(requestUrl) {
  const requestPath = new URL(requestUrl ?? "/", "http://localhost").pathname;
  const relativePath = requestPath === "/" ? "index.html" : decodeURIComponent(requestPath).replace(/^\/+/, "");
  const filePath = resolve(assetRoot, relativePath);
  if (filePath !== assetRoot && !filePath.startsWith(assetRoot + sep)) {
    return null;
  }

  return filePath;
}

const server = createServer(async (request, response) => {
  const filePath = assetPath(request.url);
  if (filePath === null) {
    response.writeHead(403).end("Forbidden");
    return;
  }

  try {
    const contents = await readFile(filePath);
    const contentType = contentTypes[extname(filePath)] ?? "application/octet-stream";
    response.writeHead(200, { "content-type": contentType });
    response.end(contents);
  } catch {
    response.writeHead(404).end("Not found");
  }
});

server.listen(port, host, () => {
  console.log("Copeland website ready at http://" + host + ":" + port);
});
