"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}


const __cope_m3_record_type_r1_1 = Symbol("r1");
const __cope_m3_record_instances_r1_2 = new WeakSet();
const __cope_m3_record_field___cope_00720031002e00660030_5 = Symbol("r1.f0");
const __cope_m3_record_field___cope_00720031002e00660031_6 = Symbol("r1.f1");

function __cope_m3_record_make_r1_3(field0, field1) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r1_1]: { value: __cope_m3_record_type_r1_1, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660030_5]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660031_6]: { value: field1, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r1_2.add(value);
    return value;
}

function __cope_m3_record_require_r1_4(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r1_2.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r1_1) || value[__cope_m3_record_type_r1_1] !== __cope_m3_record_type_r1_1 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660030_5) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660031_6)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_record_type_r2_7 = Symbol("r2");
const __cope_m3_record_instances_r2_8 = new WeakSet();
const __cope_m3_record_field___cope_00720032002e00660030_11 = Symbol("r2.f0");

function __cope_m3_record_make_r2_9(field0) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r2_7]: { value: __cope_m3_record_type_r2_7, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660030_11]: { value: field0, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r2_8.add(value);
    return value;
}

function __cope_m3_record_require_r2_10(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r2_8.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r2_7) || value[__cope_m3_record_type_r2_7] !== __cope_m3_record_type_r2_7 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660030_11)) {
        __cope_m3_panic_0();
    }
}

function main() {
    const __cope_m3_record_init_12 = 40;
    const __cope_m3_record_init_13 = 2;
    const __cope_m3_record_init_14 = __cope_m3_record_make_r1_3(__cope_m3_record_init_12, __cope_m3_record_init_13);
    const envelope = __cope_m3_record_make_r2_9(__cope_m3_record_init_14);
    const __cope_m3_record_receiver_15 = envelope;
    __cope_m3_record_require_r2_10(__cope_m3_record_receiver_15);
    const __cope_m3_record_receiver_16 = __cope_m3_record_receiver_15[__cope_m3_record_field___cope_00720032002e00660030_11];
    __cope_m3_record_require_r1_4(__cope_m3_record_receiver_16);
    const __cope_m3_ordered_19 = __cope_m3_record_receiver_16[__cope_m3_record_field___cope_00720031002e00660030_5];
    const __cope_m3_record_receiver_17 = envelope;
    __cope_m3_record_require_r2_10(__cope_m3_record_receiver_17);
    const __cope_m3_record_receiver_18 = __cope_m3_record_receiver_17[__cope_m3_record_field___cope_00720032002e00660030_11];
    __cope_m3_record_require_r1_4(__cope_m3_record_receiver_18);
    return (__cope_m3_ordered_19 + __cope_m3_record_receiver_18[__cope_m3_record_field___cope_00720031002e00660031_6]);
}
