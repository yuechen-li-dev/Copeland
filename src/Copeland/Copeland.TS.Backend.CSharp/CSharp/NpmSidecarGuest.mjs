import { Console } from "node:console";
import { createInterface } from "node:readline";

const envelopePrefix = 'const $schema: string = "copeland://interop/transport/v1"; record Envelope { correlation: string; kind: string; operation: string; payload: string; } const $value = $record.Envelope(';
const frameLimit = 1024 * 1024;
const indent = (depth) => "    ".repeat(depth);

// stdout belongs to the protocol, including during package initialization.
globalThis.console = new Console(process.stderr);

function readEnvelope(line) {
    if (Buffer.byteLength(line) > frameLimit || !line.startsWith(envelopePrefix) || !line.endsWith(",});")) {
        throw new Error("Invalid sidecar envelope");
    }
    const message = JSON.parse(line.slice(envelopePrefix.length, -4) + "}");
    for (const name of ["correlation", "kind", "operation", "payload"]) {
        if (typeof message[name] !== "string") {
            throw new Error("Invalid sidecar envelope field");
        }
    }
    return message;
}

function send(correlation, kind, operation, payload) {
    const json = JSON.stringify({ correlation, kind, operation, payload });
    const frame = envelopePrefix + json.slice(0, -1) + ",});";
    if (Buffer.byteLength(frame) > frameLimit) {
        throw new Error("Sidecar envelope exceeds its byte limit");
    }
    process.stdout.write(frame + "\n");
}

function checkString(value, plan) {
    if (typeof value !== "string" || value.length > plan.maxString || Buffer.from(value).toString() !== value) {
        throw new Error("Invalid TSON string");
    }
    return value;
}

function tsonString(value, plan) {
    return JSON.stringify(checkString(value, plan)).replace(/\\\\|\\u[0-9a-f]{4}|[\u2028\u2029]/g, (escape) => {
        if (escape.length === 1) {
            return escape === "\u2028" ? "\\u2028" : "\\u2029";
        }
        return escape.slice(0, 2) + escape.slice(2).toUpperCase();
    });
}

function decode(text, plan) {
    if (Buffer.byteLength(text) > plan.maxBytes) {
        throw new Error("TSON payload exceeds its byte limit");
    }

    let cursor = 0;
    const stringToken = /"(?:[^"\\\x00-\x1f]|\\(?:["\\/bfnrt]|u[0-9a-fA-F]{4}))*"/y;
    function consume(token) {
        if (!text.startsWith(token, cursor)) {
            return false;
        }
        cursor += token.length;
        return true;
    }

    function expect(token) {
        if (!consume(token)) {
            throw new Error("Malformed TSON payload");
        }
    }

    function read(shape, depth) {
        if (shape === "boolean") {
            if (consume("true")) return true;
            expect("false");
            return false;
        }
        if (shape === "number") {
            expect('$number("');
            const bits = text.slice(cursor, cursor + 16);
            if (!/^[0-9A-F]{16}$/.test(bits)) {
                throw new Error("Invalid TSON number");
            }
            cursor += 16;
            expect('")');
            return Buffer.from(bits, "hex").readDoubleBE();
        }
        if (shape === "string") {
            stringToken.lastIndex = cursor;
            const match = stringToken.exec(text);
            if (!match) {
                throw new Error("Invalid TSON string");
            }
            cursor = stringToken.lastIndex;
            return checkString(JSON.parse(match[0]), plan);
        }
        if (shape[0] === "array") {
            const values = [];
            if (consume("[]")) {
                return values;
            }
            expect("[\n");
            while (!consume(indent(depth) + "]")) {
                if (values.length >= plan.maxArray) {
                    throw new Error("TSON array exceeds its length limit");
                }
                expect(indent(depth + 1));
                values.push(read(shape[1], depth + 1));
                expect(",\n");
            }
            return values;
        }

        const [, name, fields] = shape;
        expect("$record." + name + "({");
        if (fields.length === 0) {
            expect("})");
            return {};
        }
        expect("\n");
        const entries = [];
        for (const [field, fieldShape] of fields) {
            expect(indent(depth + 1) + JSON.stringify(field) + ": ");
            entries.push([field, read(fieldShape, depth + 1)]);
            expect(",\n");
        }
        expect(indent(depth) + "})");
        return Object.fromEntries(entries);
    }

    expect(plan.prefix);
    const value = read(plan.shape, 0);
    expect(";\n");
    if (cursor !== text.length) {
        throw new Error("Trailing TSON payload data");
    }
    return value;
}

function encode(value, plan) {
    const parts = [];
    let size = 0;
    function append(text) {
        size += Buffer.byteLength(text);
        if (size > plan.maxBytes) {
            throw new Error("TSON payload exceeds its byte limit");
        }
        parts.push(text);
    }

    function write(shape, item, depth) {
        if (shape === "boolean" && typeof item === "boolean") {
            append(String(item));
        } else if (shape === "number" && typeof item === "number") {
            const bits = Buffer.alloc(8);
            bits.writeDoubleBE(item);
            const hex = Number.isNaN(item) ? "7FF8000000000000" : bits.toString("hex").toUpperCase();
            append('$number("' + hex + '")');
        } else if (shape === "string") {
            append(tsonString(item, plan));
        } else if (shape[0] === "array" && Array.isArray(item) && item.length <= plan.maxArray) {
            if (item.length === 0) {
                append("[]");
                return;
            }
            append("[\n");
            for (const element of item) {
                append(indent(depth + 1));
                write(shape[1], element, depth + 1);
                append(",\n");
            }
            append(indent(depth) + "]");
        } else if (shape[0] === "record" && item !== null && typeof item === "object" && !Array.isArray(item)) {
            const [, name, fields] = shape;
            append("$record." + name + "({");
            if (fields.length > 0) {
                append("\n");
                for (const [field, fieldShape] of fields) {
                    if (!Object.hasOwn(item, field)) {
                        throw new Error("Missing TSON record field " + field);
                    }
                    append(indent(depth + 1) + JSON.stringify(field) + ": ");
                    write(fieldShape, item[field], depth + 1);
                    append(",\n");
                }
                append(indent(depth));
            }
            append("})");
        } else {
            throw new Error("Package value does not match its TSON shape");
        }
    }

    append(plan.prefix);
    write(plan.shape, value, 0);
    append(";\n");
    return parts.join("");
}

let connected = false;
const input = createInterface({ input: process.stdin, crlfDelay: Infinity });

async function handle(line) {
    const { correlation, kind, operation, payload } = readEnvelope(line);
    if (!connected) {
        if (correlation !== "" || kind !== "handshake" || operation !== protocol || payload !== digest) {
            throw new Error("Sidecar protocol or contract mismatch");
        }
        connected = true;
        send("", "handshake", protocol, digest);
        return;
    }
    if (correlation === "" || kind !== "request") {
        throw new Error("Unexpected sidecar envelope");
    }

    try {
        const binding = operations.get(operation);
        if (!binding) {
            throw new Error("Unknown sidecar operation");
        }
        const request = decode(payload, binding.request);
        const args = binding.request.shape[2].map(([name]) => request[name]);
        const module = await import(binding.packageName);
        const invoke = module[binding.exportName];
        if (typeof invoke !== "function") {
            throw new Error("Missing npm function " + binding.exportName);
        }

        let result;
        try {
            result = await invoke(...args);
        } catch (error) {
            send(correlation, "remote-error", "", encode({ value: error }, binding.error));
            return;
        }
        send(correlation, "ok", "", encode({ value: result }, binding.response));
    } catch {
        send(correlation, "failure", "", "");
    }
}

input.on("line", (line) => {
    handle(line).catch((error) => {
        console.error(error.message);
        input.close();
        process.stdin.destroy();
        process.exitCode = 1;
    });
});
