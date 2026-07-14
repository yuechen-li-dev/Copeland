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

function load() {
    const __cope_m3_record_init_6 = "fixture";
    const __cope_m3_record_init_7 = true;
    const settings = __cope_m3_record_make_r1_2(__cope_m3_record_init_6, __cope_m3_record_init_7);
    return settings;
}
