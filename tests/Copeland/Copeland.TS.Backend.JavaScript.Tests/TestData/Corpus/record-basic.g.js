"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}


const __cope_m3_record_type_r1_1 = Symbol("r1");
const __cope_m3_record_field___cope_00720031002e00660030_4 = Symbol("r1.f0");
const __cope_m3_record_field___cope_00720031002e00660031_5 = Symbol("r1.f1");

function __cope_m3_record_make_r1_2(field0, field1) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r1_1]: { value: __cope_m3_record_type_r1_1, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660030_4]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660031_5]: { value: field1, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(value);
}

function __cope_m3_record_require_r1_3(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r1_1) || value[__cope_m3_record_type_r1_1] !== __cope_m3_record_type_r1_1 || Object.getOwnPropertySymbols(value).length !== 3 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660030_4) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660031_5)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_record_type_r2_6 = Symbol("r2");
const __cope_m3_record_field___cope_00720032002e00660030_9 = Symbol("r2.f0");

function __cope_m3_record_make_r2_7(field0) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r2_6]: { value: __cope_m3_record_type_r2_6, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660030_9]: { value: field0, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(value);
}

function __cope_m3_record_require_r2_8(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r2_6) || value[__cope_m3_record_type_r2_6] !== __cope_m3_record_type_r2_6 || Object.getOwnPropertySymbols(value).length !== 2 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660030_9)) {
        __cope_m3_panic_0();
    }
}

function main() {
    const __cope_m3_record_init_10 = 40;
    const __cope_m3_record_init_11 = 2;
    const __cope_m3_record_init_12 = __cope_m3_record_make_r1_2(__cope_m3_record_init_10, __cope_m3_record_init_11);
    const envelope = __cope_m3_record_make_r2_7(__cope_m3_record_init_12);
    const __cope_m3_record_receiver_13 = envelope;
    __cope_m3_record_require_r2_8(__cope_m3_record_receiver_13);
    const __cope_m3_record_receiver_14 = __cope_m3_record_receiver_13[__cope_m3_record_field___cope_00720032002e00660030_9];
    __cope_m3_record_require_r1_3(__cope_m3_record_receiver_14);
    const __cope_m3_ordered_17 = __cope_m3_record_receiver_14[__cope_m3_record_field___cope_00720031002e00660030_4];
    const __cope_m3_record_receiver_15 = envelope;
    __cope_m3_record_require_r2_8(__cope_m3_record_receiver_15);
    const __cope_m3_record_receiver_16 = __cope_m3_record_receiver_15[__cope_m3_record_field___cope_00720032002e00660030_9];
    __cope_m3_record_require_r1_3(__cope_m3_record_receiver_16);
    return (__cope_m3_ordered_17 + __cope_m3_record_receiver_16[__cope_m3_record_field___cope_00720031002e00660031_5]);
}
