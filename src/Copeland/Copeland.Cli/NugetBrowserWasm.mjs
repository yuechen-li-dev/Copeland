import { dotnet } from "./_framework/dotnet.js";

let ready;

async function load() {
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    return exports.Copeland.Generated.NugetWasm;
}

self.addEventListener("message", async ({ data: { id, digest, request } }) => {
    try {
        const bridge = await (ready ??= load());
        self.postMessage({ id, response: bridge.Invoke(digest, request) });
    } catch (error) {
        self.postMessage({ id, error: String(error) });
    }
});
