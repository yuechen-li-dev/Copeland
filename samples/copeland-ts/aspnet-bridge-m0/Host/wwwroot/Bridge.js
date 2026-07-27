import { baseUrl as __copelandBridgeBaseUrl } from "./bridge-config.js";
"use strict";

function __cope_async() {
    let terminal = 0;
    let value;
    const continuations = [];
    return Object.freeze({
        get completed() { return terminal !== 0; },
        get cancelled() { return terminal === 2; },
        get panicked() { return terminal === 3; },
        get transportFailed() { return terminal === 4; },
        get value() { return value; },
        subscribe(success, cancelled, failed, panicked) { if (terminal !== 0) return true; continuations.push({ success, cancelled, failed, panicked }); return false; },
        resolve(next) { if (terminal !== 0) return; terminal = 1; value = next; const pending = continuations.splice(0); for (const continuation of pending) continuation.success(); },
        cancel() { if (terminal !== 0) return; terminal = 2; const pending = continuations.splice(0); for (const continuation of pending) continuation.cancelled(); },
        fail() { if (terminal !== 0) return; terminal = 4; const pending = continuations.splice(0); for (const continuation of pending) continuation.failed(); },
        panic() { if (terminal !== 0) return; terminal = 3; const pending = continuations.splice(0); for (const continuation of pending) continuation.panicked(); },
    });
}
function __cope_async_pending() { return __cope_async(); }

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_make_1(type, tag, payload) {
    const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
    return value;
}

const __cope_m3_record_type_r806379001_2 = Symbol("r806379001");

function __cope_m3_record_make_r806379001_4(field0, field1) {
    return { [__cope_m3_record_type_r806379001_2]: __cope_m3_record_type_r806379001_2, $f0: field0, $f1: field1 };
}

function __cope_m3_record_require_r806379001_5(value) {
    if (typeof value !== "object" || value === null || value[__cope_m3_record_type_r806379001_2] !== __cope_m3_record_type_r806379001_2 || Object.keys(value).length !== 2 || Object.getOwnPropertySymbols(value).length !== 1 || !Object.prototype.hasOwnProperty.call(value, "$f0") || !Object.prototype.hasOwnProperty.call(value, "$f1")) {
        __cope_m3_panic_0();
    }
    if (!(typeof value.$f0 === "string")) { __cope_m3_panic_0(); }
    if (!(typeof value.$f1 === "number")) { __cope_m3_panic_0(); }
}

function __cope_record_0072003800300036003300370039003000300031(field0, field1) {
    return __cope_m3_record_make_r806379001_4(field0, field1);
}

const __cope_m3_record_type_r806379002_8 = Symbol("r806379002");

function __cope_m3_record_make_r806379002_10(field0, field1) {
    return { [__cope_m3_record_type_r806379002_8]: __cope_m3_record_type_r806379002_8, $f0: field0, $f1: field1 };
}

function __cope_m3_record_require_r806379002_11(value) {
    if (typeof value !== "object" || value === null || value[__cope_m3_record_type_r806379002_8] !== __cope_m3_record_type_r806379002_8 || Object.keys(value).length !== 2 || Object.getOwnPropertySymbols(value).length !== 1 || !Object.prototype.hasOwnProperty.call(value, "$f0") || !Object.prototype.hasOwnProperty.call(value, "$f1")) {
        __cope_m3_panic_0();
    }
    if (!(typeof value.$f0 === "string")) { __cope_m3_panic_0(); }
    if (!(typeof value.$f1 === "string")) { __cope_m3_panic_0(); }
}

function __cope_record_0072003800300036003300370039003000300032(field0, field1) {
    return __cope_m3_record_make_r806379002_10(field0, field1);
}

const __cope_m3_result_type_14 = Object.freeze(Object.create(null));

function __cope_m3_result_validate_15(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_14 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_record_require_r806379002_11(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function SerializeState(request) {
    const computation = __cope_async();
    const resolveError = (kind, message) => computation.resolve(__cope_m3_make_1(__cope_m3_result_type_14, "err", [__cope_record_0072003800300036003300370039003000300032(kind, message)]));
    try {
        __cope_m3_record_require_r806379001_5(request);
    } catch {
        resolveError("malformed-request", "The request does not match the bridge contract.");
        return computation;
    }
    globalThis.fetch(__copelandBridgeBaseUrl + "/__copeland/m0/bridge/serialize-state", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ "message": request.$f0, "count": request.$f1 }) })
        .then(async response => {
            let envelope;
            try { envelope = await response.json(); } catch { resolveError("malformed-response", "The bridge response was not valid JSON."); return; }
            if (!response.ok) { resolveError(typeof envelope?.error?.kind === "string" ? envelope.error.kind : "http-failure", "The bridge host rejected the request."); return; }
            if (envelope?.schemaVersion !== 1) { resolveError("bridge-version-mismatch", "The bridge contract version is incompatible."); return; }
            if (envelope?.schemaVersion !== 1 || envelope?.ok !== true || typeof envelope.value !== "string") { resolveError("malformed-response", "The bridge response did not match the contract."); return; }
            computation.resolve(__cope_m3_make_1(__cope_m3_result_type_14, "ok", [envelope.value]));
        })
        .catch(() => resolveError("host-unavailable", "The bridge host is unavailable."));
        return computation;
}
export { SerializeState };
export { __cope_record_0072003800300036003300370039003000300031, __cope_record_0072003800300036003300370039003000300032 };
export { __cope_record_0072003800300036003300370039003000300032 as BridgeError };
export { __cope_record_0072003800300036003300370039003000300031 as SerializeRequest };
