let activeWorker;
let nextId = 0;

function start() {
    const worker = new Worker(new URL("./wasm-worker.mjs", import.meta.url), { type: "module" });
    const pending = new Map();
    const state = { worker, pending, fail };
    worker.addEventListener("message", ({ data }) => {
        const call = pending.get(data.id);
        if (!call) return;
        pending.delete(data.id);
        clearTimeout(call.timer);
        if (typeof data.response === "string") {
            call.resolve(data.response);
        } else {
            call.reject(new Error(data.error ?? "NuGet WASM worker returned no reply."));
        }
    });
    worker.addEventListener("error", event => fail(new Error(event.message || "NuGet WASM worker failed.")));
    worker.addEventListener("messageerror", () => fail(new Error("Invalid NuGet WASM worker message.")));
    return state;

    function fail(error) {
        worker.terminate();
        if (activeWorker === state) activeWorker = undefined;
        for (const call of pending.values()) {
            clearTimeout(call.timer);
            call.reject(error);
        }
        pending.clear();
    }
}

export function deliver(digest, request) {
    const state = activeWorker ??= start();
    const id = ++nextId;
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => state.fail(new Error("NuGet WASM worker timed out.")), 60000);
        state.pending.set(id, { resolve, reject, timer });
        try {
            state.worker.postMessage({ id, digest, request });
        } catch (error) {
            state.fail(error);
        }
    });
}
