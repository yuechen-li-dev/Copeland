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
    const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
    if (type === __cope_m3_type_9) __cope_m3_instances_10.add(value);
    return value;
}

const __cope_m3_record_type_r1_3 = Symbol("r1");
const __cope_m3_record_instances_r1_4 = new WeakSet();
const __cope_m3_record_field___cope_00720031002e00660030_7 = Symbol("r1.f0");
const __cope_m3_record_field___cope_00720031002e00660031_8 = Symbol("r1.f1");

function __cope_m3_record_make_r1_5(field0, field1) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r1_3]: { value: __cope_m3_record_type_r1_3, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660030_7]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660031_8]: { value: field1, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r1_4.add(value);
    return value;
}

function __cope_m3_record_require_r1_6(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r1_4.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r1_3) || value[__cope_m3_record_type_r1_3] !== __cope_m3_record_type_r1_3 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660030_7) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660031_8)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_type_9 = Object.freeze(Object.create(null));
const __cope_m3_instances_10 = new WeakSet();

function __cope_m3_validate_11(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_10.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_9 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Moved":
            if (value.$payload.length !== 1) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !((__cope_m3_record_require_r1_6(value.$payload[0]), true))) {
                __cope_m3_panic_0();
            }
            return;
        default:
            __cope_m3_panic_0();
    }
}

const __cope_m3_result_type_12 = Object.freeze(Object.create(null));

function __cope_m3_result_validate_13(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_12 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_record_require_r1_6(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function load() {
    const __cope_m3_record_init_14 = 40;
    const __cope_m3_record_init_15 = 2;
    return __cope_m3_make_2(__cope_m3_result_type_12, "ok", [__cope_m3_record_make_r1_5(__cope_m3_record_init_14, __cope_m3_record_init_15)]);
}

function main() {
    const __cope_m3_unwrap_16 = load();
    __cope_m3_result_validate_13(__cope_m3_unwrap_16);
    if (__cope_m3_unwrap_16.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_16.$payload[0]);
    }
    const event = __cope_m3_make_2(__cope_m3_type_9, "Moved", [__cope_m3_unwrap_16.$payload[0]]);
    const __cope_m3_match_17 = event;
    __cope_m3_validate_11(__cope_m3_match_17);
    let __cope_m3_match_value_18;
    switch (__cope_m3_match_17.$tag) {
        case "Moved":
        {
            const point = __cope_m3_match_17.$payload[0];
            const __cope_m3_record_receiver_19 = point;
            __cope_m3_record_require_r1_6(__cope_m3_record_receiver_19);
            const __cope_m3_ordered_21 = __cope_m3_record_receiver_19[__cope_m3_record_field___cope_00720031002e00660030_7];
            const __cope_m3_record_receiver_20 = point;
            __cope_m3_record_require_r1_6(__cope_m3_record_receiver_20);
            __cope_m3_match_value_18 = (__cope_m3_ordered_21 + __cope_m3_record_receiver_20[__cope_m3_record_field___cope_00720031002e00660031_8]);
            break;
        }
        default:
            __cope_m3_panic_0();
    }
    return __cope_m3_match_value_18;
}
