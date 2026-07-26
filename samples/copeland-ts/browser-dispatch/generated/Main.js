import { dispatch } from "@copeland/browser-v1";
import { onClick } from "@copeland/browser-v1";
import { setText } from "@copeland/browser-v1";
import { Reduce, SendIncrement, SendReset } from "./Counter.js";
import { __cope_enum_0043006f0075006e007400650072004500760065006e0074_0049006e006300720065006d0065006e0074, __cope_enum_0043006f0075006e007400650072004500760065006e0074_00520065007300650074, __cope_record_0072003200300035003600390035003000300031 } from "./Counter.js";
"use strict";

const __cope_callable_runtime_key = Symbol.for("copeland.ts.callable-runtime.v1");
let __cope_callable_runtime = globalThis[__cope_callable_runtime_key];
if (__cope_callable_runtime === undefined) {
    __cope_callable_runtime = { instances: new WeakSet(), signatures: new WeakMap(), codes: new WeakMap(), environments: new WeakMap(), environmentInstances: new WeakSet(), environmentValues: new WeakMap() };
    Object.defineProperty(globalThis, __cope_callable_runtime_key, { value: __cope_callable_runtime, writable: false, enumerable: false, configurable: false });
}
const __cope_callable_instances = __cope_callable_runtime.instances;
const __cope_callable_signatures = __cope_callable_runtime.signatures;
const __cope_callable_codes = __cope_callable_runtime.codes;
const __cope_callable_environments = __cope_callable_runtime.environments;
const __cope_callable_environment_instances = __cope_callable_runtime.environmentInstances;
const __cope_callable_environment_values = __cope_callable_runtime.environmentValues;
function __cope_callable_ref(signature, code) {
    const carrier = Object.create(null);
    __cope_callable_signatures.set(carrier, signature);
    __cope_callable_codes.set(carrier, code);
    __cope_callable_instances.add(carrier);
    return Object.freeze(carrier);
}
function __cope_callable_host(signature, hostCallable) {
    if (typeof hostCallable !== "function") throw new Error("COPE-PANIC-CALLABLE: host returned a non-callable value");
    return __cope_callable_ref(signature, (...argumentsInOrder) => hostCallable(...argumentsInOrder));
}
function __cope_callable_capture(signature, code, values) {
    const environment = Object.create(null);
    __cope_callable_environment_values.set(environment, Object.freeze(values.slice()));
    __cope_callable_environment_instances.add(environment);
    Object.freeze(environment);
    const carrier = Object.create(null);
    __cope_callable_signatures.set(carrier, signature);
    __cope_callable_codes.set(carrier, code);
    __cope_callable_environments.set(carrier, environment);
    __cope_callable_instances.add(carrier);
    return Object.freeze(carrier);
}
function __cope_callable_invoke(carrier, signature, argumentsInOrder) {
    if (!__cope_callable_instances.has(carrier) || __cope_callable_signatures.get(carrier) !== signature) throw new Error("COPE-PANIC-CALLABLE: invalid callable");
    const code = __cope_callable_codes.get(carrier);
    const environment = __cope_callable_environments.get(carrier);
    if (environment === undefined) return code(...argumentsInOrder);
    if (!__cope_callable_environment_instances.has(environment)) throw new Error("COPE-PANIC-CALLABLE: invalid environment");
    return code(...__cope_callable_environment_values.get(environment), ...argumentsInOrder);
}

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_make_1(type, tag, payload) {
    const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
    if (type === __cope_m3_type_7) __cope_m3_instances_8.add(value);
    return value;
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
        registration = { type: Symbol(id), instances: new WeakSet(), fields: new Map() };
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

const __cope_m3_record_registration_r205695001 = __cope_m3_record_registration("r205695001");
const __cope_m3_record_type_r205695001_2 = __cope_m3_record_registration_r205695001.type;
const __cope_m3_record_instances_r205695001_3 = __cope_m3_record_registration_r205695001.instances;
const __cope_m3_record_field___cope_0072003200300035003600390035003000300031002e00660030_6 = __cope_m3_record_field(__cope_m3_record_registration_r205695001, "r205695001.f0");

function __cope_m3_record_make_r205695001_4(field0) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r205695001_2]: { value: __cope_m3_record_type_r205695001_2, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_0072003200300035003600390035003000300031002e00660030_6]: { value: field0, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r205695001_3.add(value);
    return value;
}

function __cope_m3_record_require_r205695001_5(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r205695001_3.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r205695001_2) || value[__cope_m3_record_type_r205695001_2] !== __cope_m3_record_type_r205695001_2 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_0072003200300035003600390035003000300031002e00660030_6)) {
        __cope_m3_panic_0();
    }
}



const __cope_m3_type_7 = Object.freeze(Object.create(null));
const __cope_m3_instances_8 = new WeakSet();





function __cope_m3_validate_9(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_8.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_7 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Increment":
            if (value.$payload.length !== 0) {
                __cope_m3_panic_0();
            }
            return;
        case "Reset":
            if (value.$payload.length !== 0) {
                __cope_m3_panic_0();
            }
            return;
        default:
            __cope_m3_panic_0();
    }
}







function __cope_arrow_0(state) {
    const __cope_m3_ordered_19 = "count";
    const __cope_m3_ordered_18 = ("" + "Count: ");
    const __cope_m3_record_receiver_17 = state;
    __cope_m3_record_require_r205695001_5(__cope_m3_record_receiver_17);
    return setText(__cope_m3_ordered_19, (__cope_m3_ordered_18 + String(__cope_m3_record_receiver_17[__cope_m3_record_field___cope_0072003200300035003600390035003000300031002e00660030_6])));
}

function __cope_arrow_1(send) {
    return SendIncrement(send);
}

function __cope_arrow_2(send) {
    return SendReset(send);
}

function Main() {
    const __cope_m3_record_init_20 = 0;
    const send = __cope_callable_host("(named:CounterEvent)->named:void", dispatch(__cope_record_0072003200300035003600390035003000300031(__cope_m3_record_init_20), (...args) => __cope_callable_invoke(__cope_callable_ref("(record:r205695001,named:CounterEvent)->record:r205695001", Reduce), "(record:r205695001,named:CounterEvent)->record:r205695001", args), (...args) => __cope_callable_invoke(__cope_callable_ref("(record:r205695001)->named:void", __cope_arrow_0), "(record:r205695001)->named:void", args)));
    onClick("increment", (...args) => __cope_callable_invoke(__cope_callable_capture("()->named:void", __cope_arrow_1, [send]), "()->named:void", args));
    onClick("reset", (...args) => __cope_callable_invoke(__cope_callable_capture("()->named:void", __cope_arrow_2, [send]), "()->named:void", args));
}
export { Main };
