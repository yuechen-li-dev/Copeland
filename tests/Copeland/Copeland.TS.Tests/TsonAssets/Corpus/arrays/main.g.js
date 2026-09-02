"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_make_1(type, tag, payload) {
    const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
    if (type === __cope_m3_type_16) __cope_m3_instances_17.add(value);
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

const __cope_m3_record_type_r2_7 = Symbol("r2");
const __cope_m3_record_instances_r2_8 = new WeakSet();
const __cope_m3_record_field___cope_00720032002e00660030_11 = Symbol("r2.f0");
const __cope_m3_record_field___cope_00720032002e00660031_12 = Symbol("r2.f1");
const __cope_m3_record_field___cope_00720032002e00660032_13 = Symbol("r2.f2");
const __cope_m3_record_field___cope_00720032002e00660033_14 = Symbol("r2.f3");
const __cope_m3_record_field___cope_00720032002e00660034_15 = Symbol("r2.f4");

function __cope_m3_record_make_r2_9(field0, field1, field2, field3, field4) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r2_7]: { value: __cope_m3_record_type_r2_7, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660030_11]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660031_12]: { value: field1, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660032_13]: { value: field2, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660033_14]: { value: field3, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660034_15]: { value: field4, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r2_8.add(value);
    return value;
}

const __cope_m3_type_16 = Object.freeze(Object.create(null));
const __cope_m3_instances_17 = new WeakSet();

function __cope_m3_validate_18(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_17.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_16 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Off":
            if (value.$payload.length !== 0) {
                __cope_m3_panic_0();
            }
            return;
        case "On":
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

function load() {
    const __cope_m3_record_init_19 = [];
    const __cope_m3_record_init_20 = [0, -0, 3];
    const __cope_m3_record_init_21 = "first";
    const __cope_m3_ordered_23 = __cope_m3_record_make_r1_4(__cope_m3_record_init_21);
    const __cope_m3_record_init_22 = "second";
    const __cope_m3_record_init_24 = [__cope_m3_ordered_23, __cope_m3_record_make_r1_4(__cope_m3_record_init_22)];
    const __cope_m3_record_init_25 = [__cope_m3_make_1(__cope_m3_type_16, "Off", []), __cope_m3_make_1(__cope_m3_type_16, "On", [3])];
    const __cope_m3_record_init_26 = [[], [1, 2]];
    const batch = __cope_m3_record_make_r2_9(__cope_m3_record_init_19, __cope_m3_record_init_20, __cope_m3_record_init_24, __cope_m3_record_init_25, __cope_m3_record_init_26);
    return batch;
}
