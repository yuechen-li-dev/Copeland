"use strict";

const __cope_callable_instances = new WeakSet();
const __cope_callable_signatures = new WeakMap();
const __cope_callable_codes = new WeakMap();
const __cope_callable_host_carriers = new WeakMap();
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
function __cope_callable_invoke(carrier, signature, argumentsInOrder) {
    if (!__cope_callable_instances.has(carrier) || __cope_callable_signatures.get(carrier) !== signature) throw new Error("COPE-PANIC-CALLABLE: invalid callable");
    const code = __cope_callable_codes.get(carrier);
    return code(...argumentsInOrder);
}

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_make_1(type, tag, payload) {
    const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
    if (type === __cope_m3_type_19) __cope_m3_instances_20.add(value);
    return value;
}

const __cope_m3_flow_token_2 = Object.freeze(Object.create(null));

function __cope_m3_flow_value_3(value) {
    return Object.freeze(Object.assign(Object.create(null), { $flow: __cope_m3_flow_token_2, $kind: "value", $value: value }));
}

function __cope_m3_flow_handler_4(handler, error) {
    return Object.freeze(Object.assign(Object.create(null), { $flow: __cope_m3_flow_token_2, $kind: "handler", $handler: handler, $error: error }));
}

function __cope_m3_flow_function_5(error) {
    return Object.freeze(Object.assign(Object.create(null), { $flow: __cope_m3_flow_token_2, $kind: "function", $error: error }));
}

function __cope_m3_flow_validate_6(flow) {
    if (typeof flow !== "object" || flow === null || Object.getPrototypeOf(flow) !== null || !Object.isFrozen(flow) || flow.$flow !== __cope_m3_flow_token_2 || typeof flow.$kind !== "string") {
        __cope_m3_panic_0();
    }
    switch (flow.$kind) {
        case "value":
            if (!Object.prototype.hasOwnProperty.call(flow, "$value")) { __cope_m3_panic_0(); }
            return;
        case "handler":
            if (!Number.isInteger(flow.$handler) || !Object.prototype.hasOwnProperty.call(flow, "$error")) { __cope_m3_panic_0(); }
            return;
        case "function":
            if (!Object.prototype.hasOwnProperty.call(flow, "$error")) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

const __cope_m3_record_type_r1_7 = Symbol("r1");
const __cope_m3_record_instances_r1_8 = new WeakSet();
const __cope_m3_record_field___cope_00720031002e00660030_11 = Symbol("r1.f0");

function __cope_m3_record_make_r1_9(field0) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r1_7]: { value: __cope_m3_record_type_r1_7, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660030_11]: { value: field0, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r1_8.add(value);
    return value;
}

const __cope_m3_record_type_r2_12 = Symbol("r2");
const __cope_m3_record_instances_r2_13 = new WeakSet();
const __cope_m3_record_field___cope_00720032002e00660030_16 = Symbol("r2.f0");
const __cope_m3_record_field___cope_00720032002e00660031_17 = Symbol("r2.f1");
const __cope_m3_record_field___cope_00720032002e00660032_18 = Symbol("r2.f2");

function __cope_m3_record_make_r2_14(field0, field1, field2) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r2_12]: { value: __cope_m3_record_type_r2_12, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660030_16]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660031_17]: { value: field1, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660032_18]: { value: field2, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r2_13.add(value);
    return value;
}

function __cope_m3_record_require_r2_15(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r2_13.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r2_12) || value[__cope_m3_record_type_r2_12] !== __cope_m3_record_type_r2_12 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660030_16) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660031_17) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660032_18)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_type_19 = Object.freeze(Object.create(null));
const __cope_m3_instances_20 = new WeakSet();

function __cope_m3_validate_21(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_20.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_19 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "InvalidAge":
            if (value.$payload.length !== 1) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "number")) {
                __cope_m3_panic_0();
            }
            return;
        default:
            __cope_m3_panic_0();
    }
}

const __cope_m3_result_type_22 = Object.freeze(Object.create(null));

function __cope_m3_result_validate_23(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_22 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_record_require_r2_15(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_21(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function Person__constructor(name, age) {
    if ((age < 0)) {
        return __cope_m3_make_1(__cope_m3_result_type_22, "err", [__cope_m3_make_1(__cope_m3_type_19, "InvalidAge", [age])]);
    }
    const __cope_m3_record_init_24 = name;
    const __cope_m3_record_init_25 = Person__normalize(name);
    const __cope_m3_record_init_26 = age;
    return __cope_m3_make_1(__cope_m3_result_type_22, "ok", [__cope_m3_record_make_r2_14(__cope_m3_record_init_24, __cope_m3_record_init_25, __cope_m3_record_init_26)]);
}

function Person__normalize(name) {
    return name;
}

function Person__birthday(person) {
    const __cope_m3_record_source_27 = person;
    __cope_m3_record_require_r2_15(__cope_m3_record_source_27);
    const __cope_m3_record_receiver_28 = person;
    __cope_m3_record_require_r2_15(__cope_m3_record_receiver_28);
    const __cope_m3_record_replacement_29 = (__cope_m3_record_receiver_28[__cope_m3_record_field___cope_00720032002e00660032_18] + 1);
    return __cope_m3_record_make_r2_14(__cope_m3_record_source_27[__cope_m3_record_field___cope_00720032002e00660030_16], __cope_m3_record_source_27[__cope_m3_record_field___cope_00720032002e00660031_17], __cope_m3_record_replacement_29);
}

function Person__identity__primitive_number__7F14E33913752003(value) {
    return value;
}

function label__record_Person__66BA86D43998F2CA(value) {
    const __cope_m3_record_receiver_30 = value;
    __cope_m3_record_require_r2_15(__cope_m3_record_receiver_30);
    return __cope_m3_record_receiver_30[__cope_m3_record_field___cope_00720032002e00660030_16];
}

function main() {
    const __cope_m3_function_flow_31 = (() => {
        const __cope_m3_try_protected_32 = (() => {
            const __cope_m3_propagate_35 = Person__constructor("Ada", 41);
            __cope_m3_result_validate_23(__cope_m3_propagate_35);
            if (__cope_m3_propagate_35.$tag === "err") {
                return __cope_m3_flow_handler_4(1, __cope_m3_propagate_35.$payload[0]);
            }
            const person = __cope_m3_propagate_35.$payload[0];
            const __cope_m3_record_init_36 = [person];
            const envelope = __cope_m3_record_make_r1_9(__cope_m3_record_init_36);
            const birthday = __cope_callable_ref("(record:r2)->record:r2", Person__birthday);
            const older = __cope_callable_invoke(birthday, "(record:r2)->record:r2", [person]);
            const __cope_m3_record_receiver_37 = older;
            __cope_m3_record_require_r2_15(__cope_m3_record_receiver_37);
            const age = Person__identity__primitive_number__7F14E33913752003(__cope_m3_record_receiver_37[__cope_m3_record_field___cope_00720032002e00660032_18]);
            return __cope_m3_flow_value_3(label__record_Person__66BA86D43998F2CA(older));
        })();
        __cope_m3_flow_validate_6(__cope_m3_try_protected_32);
        let __cope_m3_try_value_34;
        if (__cope_m3_try_protected_32.$kind === "handler" && __cope_m3_try_protected_32.$handler === 1) {
            const error = __cope_m3_try_protected_32.$error;
            const __cope_m3_try_handler_33 = (() => {
                return __cope_m3_flow_value_3("recovered");
            })();
            __cope_m3_flow_validate_6(__cope_m3_try_handler_33);
            if (__cope_m3_try_handler_33.$kind !== "value") {
                return __cope_m3_try_handler_33;
            }
            __cope_m3_try_value_34 = __cope_m3_try_handler_33.$value;
        } else if (__cope_m3_try_protected_32.$kind === "value") {
            __cope_m3_try_value_34 = __cope_m3_try_protected_32.$value;
        } else {
            return __cope_m3_try_protected_32;
        }
        return __cope_m3_flow_value_3(__cope_m3_try_value_34);
        return __cope_m3_flow_value_3(undefined);
    })();
    __cope_m3_flow_validate_6(__cope_m3_function_flow_31);
    if (__cope_m3_function_flow_31.$kind === "value") {
        return __cope_m3_function_flow_31.$value;
    }
    if (__cope_m3_function_flow_31.$kind === "function") {
        __cope_m3_panic_0();
    }
    __cope_m3_panic_0();
}
