export async function deliver(digest, request, maximumBytes) {
    const response = await fetch(new URL("./invoke", import.meta.url), {
        method: "POST",
        headers: { "Content-Type": "text/plain; charset=utf-8", "X-Copeland-Contract": digest },
        body: request,
        mode: "same-origin",
        redirect: "error",
        cache: "no-store",
        signal: AbortSignal.timeout(10000),
    });
    if (!response.ok || !response.body) {
        throw new Error(`NuGet service returned HTTP ${response.status}.`);
    }
    const reader = response.body.getReader();
    const decoder = new TextDecoder("utf-8", { fatal: true });
    let bytes = 0;
    let text = "";
    try {
        while (true) {
            const chunk = await reader.read();
            if (chunk.done) break;
            bytes += chunk.value.byteLength;
            if (bytes > maximumBytes) {
                await reader.cancel();
                throw new Error("NuGet service response exceeds its byte limit.");
            }
            text += decoder.decode(chunk.value, { stream: true });
        }
        return text + decoder.decode();
    } finally {
        reader.releaseLock();
    }
}
