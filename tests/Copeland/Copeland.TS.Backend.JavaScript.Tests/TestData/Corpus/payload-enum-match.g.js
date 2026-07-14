"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_make_1(type, tag, payload) {
    const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
    if (type === __cope_m3_type_2) __cope_m3_instances_3.add(value);
    if (type === __cope_m3_type_5) __cope_m3_instances_6.add(value);
    return value;
}

const __cope_m3_type_2 = Object.freeze(Object.create(null));
const __cope_m3_instances_3 = new WeakSet();

const __cope_m3_type_5 = Object.freeze(Object.create(null));
const __cope_m3_instances_6 = new WeakSet();

function __cope_m3_validate_4(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_3.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_2 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "None":
            if (value.$payload.length !== 0) {
                __cope_m3_panic_0();
            }
            return;
        case "Number":
            if (value.$payload.length !== 1) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "number")) {
                __cope_m3_panic_0();
            }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_validate_7(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_6.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_5 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Empty":
            if (value.$payload.length !== 0) {
                __cope_m3_panic_0();
            }
            return;
        case "Single":
            if (value.$payload.length !== 1) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "number")) {
                __cope_m3_panic_0();
            }
            return;
        case "Pair":
            if (value.$payload.length !== 2) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "number")) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 1) || !(typeof value.$payload[1] === "string")) {
                __cope_m3_panic_0();
            }
            return;
        case "Nested":
            if (value.$payload.length !== 1) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !((__cope_m3_validate_4(value.$payload[0]), true))) {
                __cope_m3_panic_0();
            }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function main() {
    const outer = __cope_m3_make_1(__cope_m3_type_5, "Nested", [__cope_m3_make_1(__cope_m3_type_2, "Number", [9])]);
    return (() => { const __cope_m3_match_8 = outer; __cope_m3_validate_7(__cope_m3_match_8); switch (__cope_m3_match_8.$tag) { case "Empty": { return "empty"; } case "Single": { const value = __cope_m3_match_8.$payload[0]; return "single"; } case "Pair": { const first = __cope_m3_match_8.$payload[0]; const second = __cope_m3_match_8.$payload[1]; return second; } case "Nested": { const inner = __cope_m3_match_8.$payload[0]; return (() => { const __cope_m3_match_9 = inner; __cope_m3_validate_4(__cope_m3_match_9); switch (__cope_m3_match_9.$tag) { case "None": { return "none"; } case "Number": { const value = __cope_m3_match_9.$payload[0]; return "nested"; } default: return __cope_m3_panic_0(); } })(); } default: return __cope_m3_panic_0(); } })();
}
