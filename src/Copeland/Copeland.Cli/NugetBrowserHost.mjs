import { deliver } from "./transport.mjs";

let nextCorrelation = 0;

export async function callNuget(operation, payload) {
    if (typeof payload !== "string") {
        throw new Error("NuGet browser request did not encode as TSON.");
    }
    const correlation = String(++nextCorrelation);
    const request = frame(correlation, "request", operation, payload);
    const reply = readFrame(await deliver(digest, request, maximumBytes));
    if (reply.correlation !== correlation || reply.operation !== ""
        || (reply.kind !== "ok" && reply.kind !== "remote-error")) {
        throw new Error("NuGet browser transport rejected the request.");
    }
    return reply;
}
