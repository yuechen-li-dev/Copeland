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

function load() {
    const __cope_m3_record_init_7 = "fixture";
    const __cope_m3_record_init_8 = true;
    const settings = __cope_m3_record_make_r1_3(__cope_m3_record_init_7, __cope_m3_record_init_8);
    return settings;
}
