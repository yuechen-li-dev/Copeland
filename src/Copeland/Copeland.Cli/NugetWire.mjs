const maximumBytes = 1024 * 1024;
const wireEncoder = new TextEncoder();
const wirePrefix = 'const $schema: string = "copeland://interop/transport/v1"; record Envelope { correlation: string; kind: string; operation: string; payload: string; } const $value = $record.Envelope(';

function frame(correlation, kind, operation, payload) {
    const value = wirePrefix + JSON.stringify({ correlation, kind, operation, payload }).slice(0, -1) + ",});";
    if (wireEncoder.encode(value).length > maximumBytes) {
        throw new Error("NuGet transport frame exceeds its byte limit.");
    }
    return value;
}

function readFrame(line) {
    if (typeof line !== "string" || !line.startsWith(wirePrefix) || !line.endsWith(",});")
        || wireEncoder.encode(line).length > maximumBytes) {
        throw new Error("Invalid NuGet transport frame.");
    }
    const value = JSON.parse(line.slice(wirePrefix.length, -4) + "}");
    for (const property of ["correlation", "kind", "operation", "payload"]) {
        if (typeof value[property] !== "string") {
            throw new Error("Invalid NuGet transport frame field.");
        }
    }
    return value;
}
