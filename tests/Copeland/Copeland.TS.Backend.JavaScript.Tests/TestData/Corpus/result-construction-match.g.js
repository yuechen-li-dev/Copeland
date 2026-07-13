"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_make_1(type, tag, payload) {
    return Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
}

const __cope_m3_result_type_2 = Object.freeze(Object.create(null));

function __cope_m3_result_validate_3(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_2 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!(typeof value.$payload[0] === "number")) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function good() {
    return __cope_m3_make_1(__cope_m3_result_type_2, "ok", [4]);
}

function bad() {
    return __cope_m3_make_1(__cope_m3_result_type_2, "err", ["bad"]);
}

function inspect(value) {
    const __cope_m3_result_match_4 = value;
    __cope_m3_result_validate_3(__cope_m3_result_match_4);
    let __cope_m3_result_value_5;
    switch (__cope_m3_result_match_4.$tag) {
        case "ok": {
            const numberValue = __cope_m3_result_match_4.$payload[0];
            __cope_m3_result_value_5 = numberValue;
            break;
        }
        case "err": {
            const error = __cope_m3_result_match_4.$payload[0];
            __cope_m3_result_value_5 = 0;
            break;
        }
        default:
            __cope_m3_panic_0();
    }
    return __cope_m3_result_value_5;
}
