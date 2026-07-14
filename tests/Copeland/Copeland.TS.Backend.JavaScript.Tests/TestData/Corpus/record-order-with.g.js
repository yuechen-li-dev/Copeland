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

function first() {
    return 40;
}

function second() {
    return 2;
}

function main() {
    const __cope_m3_record_init_6 = second();
    const __cope_m3_record_init_7 = first();
    let point = __cope_m3_record_make_r1_2(__cope_m3_record_init_7, __cope_m3_record_init_6);
    const __cope_m3_record_source_8 = point;
    __cope_m3_record_require_r1_3(__cope_m3_record_source_8);
    const __cope_m3_record_replacement_9 = second();
    const __cope_m3_record_replacement_10 = first();
    (point = __cope_m3_record_make_r1_2(__cope_m3_record_replacement_10, __cope_m3_record_replacement_9));
    const __cope_m3_record_receiver_11 = point;
    __cope_m3_record_require_r1_3(__cope_m3_record_receiver_11);
    const __cope_m3_ordered_13 = __cope_m3_record_receiver_11[__cope_m3_record_field___cope_00720031002e00660030_4];
    const __cope_m3_record_receiver_12 = point;
    __cope_m3_record_require_r1_3(__cope_m3_record_receiver_12);
    return (__cope_m3_ordered_13 + __cope_m3_record_receiver_12[__cope_m3_record_field___cope_00720031002e00660031_5]);
}
