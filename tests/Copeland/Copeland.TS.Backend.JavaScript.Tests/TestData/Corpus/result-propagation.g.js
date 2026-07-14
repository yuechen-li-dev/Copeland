"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_make_1(type, tag, payload) {
    const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
    return value;
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

function stored() {
    const outcome = good();
    const __cope_m3_propagate_4 = outcome;
    __cope_m3_result_validate_3(__cope_m3_propagate_4);
    if (__cope_m3_propagate_4.$tag === "err") {
        return __cope_m3_make_1(__cope_m3_result_type_2, "err", [__cope_m3_propagate_4.$payload[0]]);
    }
    const value = __cope_m3_propagate_4.$payload[0];
    return __cope_m3_make_1(__cope_m3_result_type_2, "ok", [(value + 1)]);
}
