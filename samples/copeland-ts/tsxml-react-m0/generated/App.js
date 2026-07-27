import { createElement } from "react";
import { __cope_enum_004100700070004500760065006e0074_0049006e006300720065006d0065006e0074, __cope_record_0072003100340037003600300034003000300031 } from "./State.js";
"use strict";

const __cope_callable_runtime_key = Symbol.for("copeland.ts.callable-runtime.v1");
let __cope_callable_runtime = globalThis[__cope_callable_runtime_key];
if (__cope_callable_runtime === undefined) {
    __cope_callable_runtime = { instances: new WeakSet(), signatures: new WeakMap(), codes: new WeakMap(), environments: new WeakMap(), environmentInstances: new WeakSet(), environmentValues: new WeakMap(), hostCarriers: new WeakMap() };
    Object.defineProperty(globalThis, __cope_callable_runtime_key, { value: __cope_callable_runtime, writable: false, enumerable: false, configurable: false });
}
else if (__cope_callable_runtime.hostCarriers === undefined) {
    __cope_callable_runtime.hostCarriers = new WeakMap();
}
const __cope_callable_instances = __cope_callable_runtime.instances;
const __cope_callable_signatures = __cope_callable_runtime.signatures;
const __cope_callable_codes = __cope_callable_runtime.codes;
const __cope_callable_environments = __cope_callable_runtime.environments;
const __cope_callable_environment_instances = __cope_callable_runtime.environmentInstances;
const __cope_callable_environment_values = __cope_callable_runtime.environmentValues;
const __cope_callable_host_carriers = __cope_callable_runtime.hostCarriers;
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
function __cope_callable_host_retained(signature, hostCallable) {
    if (typeof hostCallable !== "function") throw new Error("COPE-PANIC-CALLABLE: host supplied a non-callable callback argument");
    let bySignature = __cope_callable_host_carriers.get(hostCallable);
    if (bySignature === undefined) {
        bySignature = new Map();
        __cope_callable_host_carriers.set(hostCallable, bySignature);
    }
    let carrier = bySignature.get(signature);
    if (carrier === undefined) {
        carrier = __cope_callable_host(signature, hostCallable);
        bySignature.set(signature, carrier);
    }
    return carrier;
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

const __cope_m3_record_registration_r147604001 = __cope_m3_record_registration("r147604001");
const __cope_m3_record_type_r147604001_2 = __cope_m3_record_registration_r147604001.type;
const __cope_m3_record_instances_r147604001_3 = __cope_m3_record_registration_r147604001.instances;

function __cope_m3_record_make_r147604001_4(field0) {
    return { [__cope_m3_record_type_r147604001_2]: __cope_m3_record_type_r147604001_2, $f0: field0 };
}

function __cope_m3_record_require_r147604001_5(value) {
    if (typeof value !== "object" || value === null || value[__cope_m3_record_type_r147604001_2] !== __cope_m3_record_type_r147604001_2 || Object.keys(value).length !== 1 || Object.getOwnPropertySymbols(value).length !== 1 || !Object.prototype.hasOwnProperty.call(value, "$f0")) {
        __cope_m3_panic_0();
    }
    if (!(typeof value.$f0 === "number")) { __cope_m3_panic_0(); }
}



const __cope_m3_type_7 = Symbol("AppEvent");
const __cope_m3_type_7_case_0 = Object.freeze({ $type: __cope_m3_type_7, $tag: "Increment" });



function __cope_m3_validate_9(value) {
    if (typeof value !== "object" || value === null || value.$type !== __cope_m3_type_7 || typeof value.$tag !== "string") {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Increment":
            if (Object.keys(value).length !== 2 || Object.getOwnPropertySymbols(value).length !== 0) {
                __cope_m3_panic_0();
            }
            return;
        default:
            __cope_m3_panic_0();
    }
}





function __cope_App_tsx___cope_arrow_0(send, increment) {
    return __cope_callable_invoke(send, "(named:AppEvent)->named:void", [increment]);
}

function View(state, send) {
    if (!((__cope_m3_record_require_r147604001_5(state), true))) { __cope_m3_panic_0(); }
    if (!(__cope_callable_instances.has(send) && __cope_callable_signatures.get(send) === "(named:AppEvent)->named:void")) { __cope_m3_panic_0(); }
    const countPrefix = "Count: ";
    const increment = __cope_enum_004100700070004500760065006e0074_0049006e006300720065006d0065006e0074();
    const __cope_m3_record_receiver_16 = state;
    return createElement("main", null, createElement("h1", null, "Copeland TS + React"), createElement("p", { id: "count" }, countPrefix, __cope_m3_record_receiver_16.$f0), createElement("button", { id: "increment", onClick: () => __cope_callable_invoke(__cope_callable_capture("()->named:void", __cope_App_tsx___cope_arrow_0, [send, increment]), "()->named:void", []) }, "Increment"));
}
export { View };
