import { camelCase } from "lodash-es";
import { createInterface } from "node:readline";

function escapeTson(value) {
    return value
        .replaceAll("\\", "\\\\")
        .replaceAll("\"", "\\\"")
        .replaceAll("\n", "\\n")
        .replaceAll("\r", "\\r")
        .replaceAll("\t", "\\t");
}

function envelope(correlation, kind, operation, payload) {
    return `const $schema: string = "copeland://interop/transport/v1"; record Envelope { correlation: string; kind: string; operation: string; payload: string; } const $value = $record.Envelope({"correlation":"${escapeTson(correlation)}","kind":"${escapeTson(kind)}","operation":"${escapeTson(operation)}","payload":"${escapeTson(payload)}",});`;
}

function readEnvelopeField(frame, name) {
    const match = new RegExp(`"${name}":"((?:\\\\.|[^"])*)"`).exec(frame);
    if (!match) {
        throw new Error(`Missing ${name} in Copeland sidecar frame.`);
    }

    return JSON.parse(`"${match[1]}"`);
}

function readArgument(payload) {
    const match = /"arg0":\s*"((?:\\.|[^"])*)"/.exec(payload);
    if (!match) {
        throw new Error("Missing arg0 in Copeland npm request.");
    }

    return JSON.parse(`"${match[1]}"`);
}

function response(value) {
    return `const $schema: string = "copeland://preview/hello";
record __NpmTransport_response_cbd5f4a3e6544793 {
    value: string;
}
const $value = $record.__NpmTransport_response_cbd5f4a3e6544793({
    "value": "${escapeTson(value)}",
});
`;
}

const lines = createInterface({ input: process.stdin });
lines.on("line", (frame) => {
    const correlation = readEnvelopeField(frame, "correlation");
    const kind = readEnvelopeField(frame, "kind");
    if (kind === "handshake") {
        process.stdout.write(envelope(
            "",
            "handshake",
            readEnvelopeField(frame, "operation"),
            readEnvelopeField(frame, "payload")) + "\n");
        return;
    }

    const operation = readEnvelopeField(frame, "operation");
    if (operation !== "npm:lodash-es@4.18.1:camelCase") {
        process.stdout.write(envelope(correlation, "failure", "", "") + "\n");
        return;
    }

    const payload = readEnvelopeField(frame, "payload");
    process.stdout.write(envelope(
        correlation,
        "ok",
        "",
        response(camelCase(readArgument(payload)))) + "\n");
});
