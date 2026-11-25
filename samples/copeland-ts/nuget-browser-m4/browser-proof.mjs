import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { cp, mkdtemp, readFile, rm } from "node:fs/promises";
import { createServer, request } from "node:http";
import { once } from "node:events";
import { createRequire } from "node:module";
import { extname, join, resolve, sep } from "node:path";

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.TSPACK_PLAYWRIGHT_MODULE ?? "playwright");
const dist = resolve(process.argv[2] ?? "dist");
const deployed = await mkdtemp(join(dist, "../.m4-proof-"));
const endpoint = "/copeland-nuget/invoke";
const types = { ".js": "text/javascript", ".mjs": "text/javascript", ".json": "application/json", ".wasm": "application/wasm" };
let service;
let server;
let browser;

try {
    for (const mode of ["wasm", "service"]) {
        await cp(join(dist, mode), join(deployed, mode), { recursive: true });
    }

    browser = await chromium.launch();
    for (const mode of ["wasm", "service"]) {
        const serviceUrl = mode === "service" ? await startService() : null;
        const seen = [];
        server = createServer(async (incoming, outgoing) => {
            try {
                const path = new URL(incoming.url, "http://127.0.0.1").pathname;
                seen.push([incoming.method, path]);
                if (serviceUrl && path === "/fixture" + endpoint && incoming.method === "POST") {
                    const upstream = request(new URL(endpoint, serviceUrl), {
                        method: "POST",
                        headers: incoming.headers,
                    }, reply => {
                        outgoing.writeHead(reply.statusCode, reply.headers);
                        reply.pipe(outgoing);
                    });
                    upstream.on("error", () => outgoing.writeHead(502).end());
                    incoming.pipe(upstream);
                    return;
                }
                if (path === "/fixture/") {
                    outgoing.writeHead(200, { "Content-Type": "text/html" });
                    outgoing.end("<!doctype html><title>NuGet browser proof</title>");
                    return;
                }
                const root = join(deployed, mode);
                const file = resolve(root, path.slice("/fixture/".length));
                if (incoming.method !== "GET" || !path.startsWith("/fixture/") || !file.startsWith(root + sep)
                    || path.startsWith("/fixture/copeland-nuget/service/")) {
                    outgoing.writeHead(404).end();
                    return;
                }
                const data = await readFile(file);
                outgoing.writeHead(200, { "Content-Type": types[extname(file)] ?? "application/octet-stream" });
                outgoing.end(data);
            } catch (error) {
                outgoing.writeHead(error.code === "ENOENT" ? 404 : 500).end();
            }
        });
        server.listen(0, "127.0.0.1");
        await once(server, "listening");
        const origin = `http://127.0.0.1:${server.address().port}/fixture/`;

        const page = await browser.newPage();
        let workers = 0;
        let capturedRequest;
        page.on("worker", () => workers++);
        page.on("request", request => {
            if (request.method() === "POST" && new URL(request.url()).pathname === "/fixture" + endpoint) {
                capturedRequest = request;
            }
        });
        await page.goto(origin);
        assert.equal(await page.evaluate(async () => (await import("./main.js")).result), '"Ship the docs" | true | typed error');
        const posts = seen.filter(([method]) => method === "POST");
        if (mode === "wasm") {
            assert.equal(workers, 1);
            assert.equal(posts.length, 0);
            assert(seen.some(([, path]) => path.includes("/_framework/Newtonsoft.Json.") && path.endsWith(".wasm")));
            assert(seen.some(([, path]) => path.includes("/_framework/Humanizer.") && path.endsWith(".wasm")));
        } else {
            assert.equal(workers, 0);
            assert.equal(posts.length, 3);
            assert(posts.every(([, path]) => path === "/fixture" + endpoint));
            assert(!seen.some(([, path]) => path.includes("/_framework/")));
            assert(capturedRequest);
            const stale = await fetch(origin + "copeland-nuget/invoke", {
                method: "POST",
                headers: { "X-Copeland-Contract": "stale" },
                body: capturedRequest.postData(),
            });
            assert.equal(stale.status, 400);
            const oversized = await fetch(origin + "copeland-nuget/invoke", {
                method: "POST",
                headers: { "X-Copeland-Contract": capturedRequest.headers()["x-copeland-contract"] },
                body: "x".repeat(1024 * 1024 + 1),
            });
            assert.equal(oversized.status, 413);
        }
        await page.close();

        const brokenPage = await browser.newPage();
        await brokenPage.route(mode === "wasm" ? "**/wasm-worker.mjs" : "**/invoke", route => route.abort());
        await brokenPage.goto(origin);
        await assert.rejects(brokenPage.evaluate(async () => (await import("./main.js")).result), /Copeland entry transport failed/);
        await brokenPage.close();
        await new Promise(resolveClose => server.close(resolveClose));
        server = undefined;
        console.log(`${mode}: published Newtonsoft.Json calls, typed package error, and transport failure verified`);
    }
} finally {
    await browser?.close();
    server?.close();
    if (service?.pid && service.exitCode === null && service.signalCode === null) {
        const exited = once(service, "exit");
        service.kill();
        await exited;
    }
    await rm(deployed, { recursive: true, force: true });
}

async function startService() {
    service = spawn("dotnet", [join(deployed, "service", "copeland-nuget", "service", "NugetWorker.dll"),
        "--urls", "http://127.0.0.1:0"], { stdio: ["ignore", "pipe", "pipe"], windowsHide: true });
    return new Promise((resolveUrl, reject) => {
        let output = "";
        const timer = setTimeout(() => reject(new Error(output || "The NuGet service did not start.")), 15000);
        const append = chunk => {
            output += chunk;
            const match = output.match(/Now listening on:\s+(http:\/\/127\.0\.0\.1:\d+)/);
            if (match) {
                clearTimeout(timer);
                resolveUrl(match[1]);
            }
        };
        service.stdout.on("data", append);
        service.stderr.on("data", append);
        service.once("error", error => {
            clearTimeout(timer);
            reject(error);
        });
        service.once("exit", () => {
            clearTimeout(timer);
            reject(new Error(output || "The NuGet service exited."));
        });
    });
}
