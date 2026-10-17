"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}


const __cope_m3_record_registry_key = Symbol.for("copeland.ts.record-registry.v1");
let __cope_m3_record_registry = globalThis[__cope_m3_record_registry_key];
if (__cope_m3_record_registry === undefined) {
    __cope_m3_record_registry = new Map();
    Object.defineProperty(globalThis, __cope_m3_record_registry_key, { value: __cope_m3_record_registry, writable: false, enumerable: false, configurable: false });
}
function __cope_m3_record_registration(id) {
    let registration = __cope_m3_record_registry.get(id);
    if (registration === undefined) {
        registration = { type: Symbol.for("copeland.ts.record." + id), instances: new WeakSet(), fields: new Map() };
        __cope_m3_record_registry.set(id, registration);
    }
    return registration;
}
function __cope_m3_record_field(registration, id) {
    let field = registration.fields.get(id);
    if (field === undefined) {
        field = Symbol(id);
        registration.fields.set(id, field);
    }
    return field;
}

const __cope_m3_record_registration_r169457001 = __cope_m3_record_registration("r169457001");
const __cope_m3_record_type_r169457001_1 = __cope_m3_record_registration_r169457001.type;
const __cope_m3_record_instances_r169457001_2 = __cope_m3_record_registration_r169457001.instances;

function __cope_m3_record_make_r169457001_3(field0) {
    return { [__cope_m3_record_type_r169457001_1]: __cope_m3_record_type_r169457001_1, $f0: field0 };
}

function __cope_record_0072003100360039003400350037003000300031(field0) {
    return __cope_m3_record_make_r169457001_3(field0);
}

function answer() {
    const __cope_m3_record_init_6 = 42;
    const data = __cope_record_0072003100360039003400350037003000300031(__cope_m3_record_init_6);
    const __cope_m3_record_receiver_7 = data;
    return __cope_m3_record_receiver_7.$f0;
}
export { answer };
