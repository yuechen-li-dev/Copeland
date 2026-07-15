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

function identity__primitive_number__8F3BAE8FF0D9F338(value) {
    return value;
}

function sum__record_Point__AD972DE51050F4B6(value) {
    const __cope_m3_record_receiver_7 = value;
    __cope_m3_record_require_r1_4(__cope_m3_record_receiver_7);
    const __cope_m3_ordered_9 = __cope_m3_record_receiver_7[__cope_m3_record_field___cope_00720031002e00660030_5];
    const __cope_m3_record_receiver_8 = value;
    __cope_m3_record_require_r1_4(__cope_m3_record_receiver_8);
    return (__cope_m3_ordered_9 + __cope_m3_record_receiver_8[__cope_m3_record_field___cope_00720031002e00660031_6]);
}

function main() {
    const __cope_m3_record_init_10 = 20;
    const __cope_m3_record_init_11 = 22;
    const point = __cope_m3_record_make_r1_3(__cope_m3_record_init_10, __cope_m3_record_init_11);
    const explicit = identity__primitive_number__8F3BAE8FF0D9F338(sum__record_Point__AD972DE51050F4B6(point));
    const inferred = identity__primitive_number__8F3BAE8FF0D9F338(sum__record_Point__AD972DE51050F4B6(point));
    return (explicit + inferred);
}
