"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}


const __cope_m3_record_type_r1_1 = Symbol("r1");

function __cope_m3_record_make_r1_3(field0) {
    return { [__cope_m3_record_type_r1_1]: __cope_m3_record_type_r1_1, $f0: field0 };
}

function answer() {
    const __cope_m3_record_init_6 = 42;
    const data = __cope_m3_record_make_r1_3(__cope_m3_record_init_6);
    const __cope_m3_record_receiver_7 = data;
    return __cope_m3_record_receiver_7.$f0;
}
