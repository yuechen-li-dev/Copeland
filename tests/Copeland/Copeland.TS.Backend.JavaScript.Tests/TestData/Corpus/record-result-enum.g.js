"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_panic_unwrap_1(error) {
    const panic = new Error("COPE-PANIC-UNWRAP: Result unwrap encountered err");
    panic.error = error;
    throw panic;
}

function __cope_m3_make_2(type, tag, payload) {
    return Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
}

const __cope_m3_record_type_r1_3 = Symbol("r1");
const __cope_m3_record_field___cope_00720031002e00660030_6 = Symbol("r1.f0");
const __cope_m3_record_field___cope_00720031002e00660031_7 = Symbol("r1.f1");

function __cope_m3_record_make_r1_4(field0, field1) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r1_3]: { value: __cope_m3_record_type_r1_3, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660030_6]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660031_7]: { value: field1, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(value);
}

function __cope_m3_record_require_r1_5(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r1_3) || value[__cope_m3_record_type_r1_3] !== __cope_m3_record_type_r1_3 || Object.getOwnPropertySymbols(value).length !== 3 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660030_6) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660031_7)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_type_8 = Object.freeze(Object.create(null));

function __cope_m3_validate_9(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_8 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Moved":
            if (value.$payload.length !== 1) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !((__cope_m3_record_require_r1_5(value.$payload[0]), true))) {
                __cope_m3_panic_0();
            }
            return;
        default:
            __cope_m3_panic_0();
    }
}

const __cope_m3_result_type_10 = Object.freeze(Object.create(null));

function __cope_m3_result_validate_11(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_10 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_record_require_r1_5(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function load() {
    const __cope_m3_record_init_12 = 40;
    const __cope_m3_record_init_13 = 2;
    return __cope_m3_make_2(__cope_m3_result_type_10, "ok", [__cope_m3_record_make_r1_4(__cope_m3_record_init_12, __cope_m3_record_init_13)]);
}

function main() {
    const __cope_m3_unwrap_14 = load();
    __cope_m3_result_validate_11(__cope_m3_unwrap_14);
    if (__cope_m3_unwrap_14.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_14.$payload[0]);
    }
    const event = __cope_m3_make_2(__cope_m3_type_8, "Moved", [__cope_m3_unwrap_14.$payload[0]]);
    const __cope_m3_match_15 = event;
    __cope_m3_validate_9(__cope_m3_match_15);
    let __cope_m3_match_value_16;
    switch (__cope_m3_match_15.$tag) {
        case "Moved":
        {
            const point = __cope_m3_match_15.$payload[0];
            const __cope_m3_record_receiver_17 = point;
            __cope_m3_record_require_r1_5(__cope_m3_record_receiver_17);
            const __cope_m3_ordered_19 = __cope_m3_record_receiver_17[__cope_m3_record_field___cope_00720031002e00660030_6];
            const __cope_m3_record_receiver_18 = point;
            __cope_m3_record_require_r1_5(__cope_m3_record_receiver_18);
            __cope_m3_match_value_16 = (__cope_m3_ordered_19 + __cope_m3_record_receiver_18[__cope_m3_record_field___cope_00720031002e00660031_7]);
            break;
        }
        default:
            __cope_m3_panic_0();
    }
    return __cope_m3_match_value_16;
}
