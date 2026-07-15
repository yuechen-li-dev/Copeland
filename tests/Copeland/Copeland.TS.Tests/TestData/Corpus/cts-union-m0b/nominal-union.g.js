"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_make_1(type, tag, payload) {
    const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
    if (type === __cope_m3_type_13) __cope_m3_instances_14.add(value);
    return value;
}

const __cope_m3_record_type_r1_2 = Symbol("r1");
const __cope_m3_record_instances_r1_3 = new WeakSet();
const __cope_m3_record_field___cope_00720031002e00660030_6 = Symbol("r1.f0");

function __cope_m3_record_make_r1_4(field0) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r1_2]: { value: __cope_m3_record_type_r1_2, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660030_6]: { value: field0, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r1_3.add(value);
    return value;
}

function __cope_m3_record_require_r1_5(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r1_3.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r1_2) || value[__cope_m3_record_type_r1_2] !== __cope_m3_record_type_r1_2 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660030_6)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_record_type_r2_7 = Symbol("r2");
const __cope_m3_record_instances_r2_8 = new WeakSet();
const __cope_m3_record_field___cope_00720032002e00660030_11 = Symbol("r2.f0");
const __cope_m3_record_field___cope_00720032002e00660031_12 = Symbol("r2.f1");

function __cope_m3_record_make_r2_9(field0, field1) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r2_7]: { value: __cope_m3_record_type_r2_7, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660030_11]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660031_12]: { value: field1, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r2_8.add(value);
    return value;
}

function __cope_m3_record_require_r2_10(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r2_8.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r2_7) || value[__cope_m3_record_type_r2_7] !== __cope_m3_record_type_r2_7 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660030_11) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660031_12)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_type_13 = Object.freeze(Object.create(null));
const __cope_m3_instances_14 = new WeakSet();

function __cope_m3_validate_15(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_14.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_13 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Circle":
            if (value.$payload.length !== 1) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !((__cope_m3_record_require_r1_5(value.$payload[0]), true))) {
                __cope_m3_panic_0();
            }
            return;
        case "Rectangle":
            if (value.$payload.length !== 1) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !((__cope_m3_record_require_r2_10(value.$payload[0]), true))) {
                __cope_m3_panic_0();
            }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function main() {
    const __cope_m3_record_init_16 = 4;
    const circle = __cope_m3_record_make_r1_4(__cope_m3_record_init_16);
    const shape = __cope_m3_make_1(__cope_m3_type_13, "Circle", [circle]);
    const __cope_m3_match_17 = shape;
    __cope_m3_validate_15(__cope_m3_match_17);
    let __cope_m3_match_value_18;
    switch (__cope_m3_match_17.$tag) {
        case "Circle":
        {
            const value = __cope_m3_match_17.$payload[0];
            const __cope_m3_record_receiver_19 = value;
            __cope_m3_record_require_r1_5(__cope_m3_record_receiver_19);
            const __cope_m3_ordered_21 = __cope_m3_record_receiver_19[__cope_m3_record_field___cope_00720031002e00660030_6];
            const __cope_m3_record_receiver_20 = value;
            __cope_m3_record_require_r1_5(__cope_m3_record_receiver_20);
            __cope_m3_match_value_18 = (__cope_m3_ordered_21 * __cope_m3_record_receiver_20[__cope_m3_record_field___cope_00720031002e00660030_6]);
            break;
        }
        case "Rectangle":
        {
            const value = __cope_m3_match_17.$payload[0];
            const __cope_m3_record_receiver_22 = value;
            __cope_m3_record_require_r2_10(__cope_m3_record_receiver_22);
            const __cope_m3_ordered_24 = __cope_m3_record_receiver_22[__cope_m3_record_field___cope_00720032002e00660030_11];
            const __cope_m3_record_receiver_23 = value;
            __cope_m3_record_require_r2_10(__cope_m3_record_receiver_23);
            __cope_m3_match_value_18 = (__cope_m3_ordered_24 * __cope_m3_record_receiver_23[__cope_m3_record_field___cope_00720032002e00660031_12]);
            break;
        }
        default:
            __cope_m3_panic_0();
    }
    return __cope_m3_match_value_18;
}
