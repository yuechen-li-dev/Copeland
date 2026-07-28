"use strict";

const __cope_callable_instances = new WeakSet();
const __cope_callable_signatures = new WeakMap();
const __cope_callable_codes = new WeakMap();
const __cope_callable_host_carriers = new WeakMap();
const __cope_callable_environments = new WeakMap();
const __cope_callable_environment_instances = new WeakSet();
const __cope_callable_environment_values = new WeakMap();
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
    if (type === __cope_m3_type_10) __cope_m3_instances_11.add(value);
    return value;
}

const __cope_m3_record_type_r1_2 = Symbol("r1");
const __cope_m3_record_instances_r1_3 = new WeakSet();
const __cope_m3_record_field___cope_00720031002e00660030_6 = Symbol("r1.f0");

function __cope_m3_record_make_r1_4(field0) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r1_2]: { value: __cope_m3_record_type_r1_2, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660030_6]: { value: field0, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r1_3.add(value);
    return value;
}

function __cope_m3_record_require_r1_5(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r1_3.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r1_2) || value[__cope_m3_record_type_r1_2] !== __cope_m3_record_type_r1_2 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660030_6)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_type_7 = Object.freeze(Object.create(null));
const __cope_m3_instances_8 = new WeakSet();

const __cope_m3_type_10 = Object.freeze(Object.create(null));
const __cope_m3_instances_11 = new WeakSet();

function __cope_m3_validate_9(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_8.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_7 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Value":
            if (value.$payload.length !== 1) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(__cope_callable_instances.has(value.$payload[0]) && __cope_callable_signatures.get(value.$payload[0]) === "(named:number)->named:number")) {
                __cope_m3_panic_0();
            }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_validate_12(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_11.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_10 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Bad":
            if (value.$payload.length !== 0) {
                __cope_m3_panic_0();
            }
            return;
        default:
            __cope_m3_panic_0();
    }
}

const __cope_m3_result_type_13 = Object.freeze(Object.create(null));

function __cope_m3_result_validate_14(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_13 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!(__cope_callable_instances.has(value.$payload[0]) && __cope_callable_signatures.get(value.$payload[0]) === "(named:number)->named:number")) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_12(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function increment(value) {
    return (value + 1);
}

function apply(operation, value) {
    return __cope_callable_invoke(operation, "(named:number)->named:number", [value]);
}

function __cope_arrow_0(base, value) {
    return (base + value);
}

function makeAdder(base) {
    return __cope_callable_capture("(named:number)->named:number", __cope_arrow_0, [base]);
}

function identity__primitive_number__8F3BAE8FF0D9F338(value) {
    return value;
}

function __cope_arrow_1(value) {
    return (value * 2);
}

function __cope_arrow_2(value) {
    const adjusted = (value + 1);
    return (adjusted * 2);
}

function main() {
    const named = __cope_callable_ref("(named:number)->named:number", increment);
    const closed = __cope_callable_ref("(named:number)->named:number", identity__primitive_number__8F3BAE8FF0D9F338);
    const double = __cope_callable_ref("(named:number)->named:number", __cope_arrow_1);
    const block = __cope_callable_ref("(named:number)->named:number", __cope_arrow_2);
    const escaped = makeAdder(10);
    const stored = [named, closed, double, block, escaped];
    const __cope_m3_record_init_15 = escaped;
    const box = __cope_m3_record_make_r1_4(__cope_m3_record_init_15);
    const __cope_m3_record_receiver_16 = box;
    __cope_m3_record_require_r1_5(__cope_m3_record_receiver_16);
    const choice = __cope_m3_make_1(__cope_m3_type_7, "Value", [__cope_m3_record_receiver_16[__cope_m3_record_field___cope_00720031002e00660030_6]]);
    const result = __cope_m3_make_1(__cope_m3_result_type_13, "ok", [(() => { const __cope_m3_match_17 = choice; __cope_m3_validate_9(__cope_m3_match_17); switch (__cope_m3_match_17.$tag) { case "Value": { const operation = __cope_m3_match_17.$payload[0]; return operation; } default: return __cope_m3_panic_0(); } })()]);
    const __cope_m3_result_match_18 = result;
    __cope_m3_result_validate_14(__cope_m3_result_match_18);
    let __cope_m3_result_value_19;
    switch (__cope_m3_result_match_18.$tag) {
        case "ok": {
            const operation = __cope_m3_result_match_18.$payload[0];
            __cope_m3_result_value_19 = apply(operation, 1);
            break;
        }
        case "err": {
            const error = __cope_m3_result_match_18.$payload[0];
            __cope_m3_result_value_19 = 0;
            break;
        }
        default:
            __cope_m3_panic_0();
    }
    return __cope_m3_result_value_19;
}
