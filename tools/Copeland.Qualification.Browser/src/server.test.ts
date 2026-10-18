import assert from "node:assert/strict";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { resolve, sep } from "node:path";
import { pathToFileURL } from "node:url";
import test from "node:test";
import { startServer } from "./server.ts";

const outputRoot = resolve(import.meta.dirname, "../../../artifacts/local/browser-server-tests");

test("loopback host serves artifacts, refuses traversal, and releases its listener", async () => {
    await mkdir(outputRoot, { recursive: true });
    const fixture = await mkdtemp(resolve(outputRoot, "case-"));
    const publicRoot = resolve(fixture, "public");
    await mkdir(publicRoot);
    await writeFile(resolve(publicRoot, "index.html"), "<title>Qualification</title>");
    await writeFile(resolve(fixture, "private.txt"), "not public");
    const server = await startServer(pathToFileURL(publicRoot));
    try {
        const page = await fetch(`${server.url}/index.html`);
        assert.equal(page.status, 200);
        assert.equal(page.headers.get("content-type"), "text/html");
        assert.equal(await page.text(), "<title>Qualification</title>");
        assert.equal((await fetch(`${server.url}/missing.js`)).status, 404);
        assert.equal((await fetch(`${server.url}/favicon.ico`)).status, 204);
        assert.equal((await fetch(`${server.url}/..%2Fprivate.txt`)).status, 403);
    } finally {
        await server.close();
        assert.ok(fixture.startsWith(outputRoot + sep));
        await rm(fixture, { recursive: true });
    }
    await assert.rejects(fetch(`${server.url}/index.html`));
});
