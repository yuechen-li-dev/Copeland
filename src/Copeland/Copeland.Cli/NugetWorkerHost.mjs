import { spawn } from "node:child_process";
import { createInterface } from "node:readline";
import { fileURLToPath } from "node:url";

let worker;
let correlationId = 0;

function setReferenced(child, referenced) {
    for (const handle of [child, child.stdin, child.stdout]) {
        if (referenced) handle.ref();
        else handle.unref();
    }
}

function launch() {
    const dll = fileURLToPath(new URL("./worker/NugetWorker.dll", import.meta.url));
    const child = spawn("dotnet", [dll], {
        cwd: fileURLToPath(new URL("./worker/", import.meta.url)),
        stdio: ["pipe", "pipe", "inherit"],
        windowsHide: true,
    });
    const inFlight = new Map();
    const state = { child, inFlight, ready: null, users: 0, failure: null };
    let resolveHello;
    let rejectHello;
    let settled = false;
    state.ready = new Promise((resolve, reject) => {
        resolveHello = resolve;
        rejectHello = reject;
    });
    const timeout = setTimeout(() => fail(new Error("NuGet worker handshake timed out.")), 10000);

    function fail(error) {
        if (state.failure) return;
        state.failure = error;
        clearTimeout(timeout);
        rejectHello(error);
        for (const pending of inFlight.values()) pending.reject(error);
        inFlight.clear();
        child.kill();
    }

    child.once("error", fail);
    child.once("exit", () => fail(new Error("NuGet worker exited.")));
    child.stdin.on("error", fail);
    child.stdout.on("error", fail);

    const lines = createInterface({ input: child.stdout, crlfDelay: Infinity });
    lines.on("line", (line) => {
        try {
            const message = readFrame(line);
            if (!settled) {
                if (message.correlation !== "" || message.kind !== "handshake"
                    || message.operation !== protocol || message.payload !== digest) {
                    throw new Error("NuGet worker protocol or contract mismatch.");
                }
                settled = true;
                clearTimeout(timeout);
                resolveHello();
                return;
            }
            const pending = inFlight.get(message.correlation);
            if (!pending || message.operation !== "") {
                throw new Error("Unexpected NuGet worker response.");
            }
            inFlight.delete(message.correlation);
            if (message.kind === "ok" || message.kind === "remote-error") {
                pending.resolve(message);
            } else if (message.kind === "failure") {
                pending.reject(new Error("NuGet worker rejected the transport payload."));
            } else {
                pending.reject(new Error("Unexpected NuGet worker response kind."));
                throw new Error("Unexpected NuGet worker response kind.");
            }
        } catch (error) {
            fail(error);
        }
    });

    process.once("exit", () => child.kill());
    child.stdin.write(frame("", "handshake", protocol, digest) + "\n");
    return state;
}

export async function callNuget(operation, payload) {
    if (typeof payload !== "string") throw new Error("NuGet worker request did not encode as TSON.");
    const correlation = String(++correlationId);
    const request = frame(correlation, "request", operation, payload) + "\n";
    const state = worker ??= launch();
    if (state.failure) throw state.failure;
    if (++state.users === 1) setReferenced(state.child, true);
    try {
        await state.ready;
        if (state.failure) throw state.failure;
        return await new Promise((resolve, reject) => {
            state.inFlight.set(correlation, { resolve, reject });
            state.child.stdin.write(request);
        });
    } finally {
        if (--state.users === 0 && !state.failure) setReferenced(state.child, false);
    }
}
