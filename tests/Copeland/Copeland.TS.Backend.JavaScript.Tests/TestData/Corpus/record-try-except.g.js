"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_make_1(type, tag, payload) {
    const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
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
const __cope_m3_record_field___cope_00720031002e00660031_12 = Symbol("r1.f1");

function __cope_m3_record_make_r1_9(field0, field1) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r1_7]: { value: __cope_m3_record_type_r1_7, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660030_11]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660031_12]: { value: field1, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r1_8.add(value);
    return value;
}

function __cope_m3_record_require_r1_10(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r1_8.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r1_7) || value[__cope_m3_record_type_r1_7] !== __cope_m3_record_type_r1_7 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660030_11) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660031_12)) {
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
            if (!((__cope_m3_record_require_r1_10(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function bad() {
    return __cope_m3_make_1(__cope_m3_result_type_13, "err", ["bad"]);
}

function fallback() {
    const __cope_m3_record_init_15 = 40;
    const __cope_m3_record_init_16 = 2;
    return __cope_m3_record_make_r1_9(__cope_m3_record_init_15, __cope_m3_record_init_16);
}

function main() {
    const __cope_m3_function_flow_17 = (() => {
        const __cope_m3_try_protected_18 = (() => {
            const __cope_m3_propagate_21 = bad();
            __cope_m3_result_validate_14(__cope_m3_propagate_21);
            if (__cope_m3_propagate_21.$tag === "err") {
                return __cope_m3_flow_handler_4(1, __cope_m3_propagate_21.$payload[0]);
            }
            return __cope_m3_flow_value_3(__cope_m3_propagate_21.$payload[0]);
        })();
        __cope_m3_flow_validate_6(__cope_m3_try_protected_18);
        let __cope_m3_try_value_20;
        if (__cope_m3_try_protected_18.$kind === "handler" && __cope_m3_try_protected_18.$handler === 1) {
            const error = __cope_m3_try_protected_18.$error;
            const __cope_m3_try_handler_19 = (() => {
                return __cope_m3_flow_value_3(fallback());
            })();
            __cope_m3_flow_validate_6(__cope_m3_try_handler_19);
            if (__cope_m3_try_handler_19.$kind !== "value") {
                return __cope_m3_try_handler_19;
            }
            __cope_m3_try_value_20 = __cope_m3_try_handler_19.$value;
        } else if (__cope_m3_try_protected_18.$kind === "value") {
            __cope_m3_try_value_20 = __cope_m3_try_protected_18.$value;
        } else {
            return __cope_m3_try_protected_18;
        }
        const point = __cope_m3_try_value_20;
        const __cope_m3_record_receiver_22 = point;
        __cope_m3_record_require_r1_10(__cope_m3_record_receiver_22);
        const __cope_m3_ordered_24 = __cope_m3_record_receiver_22[__cope_m3_record_field___cope_00720031002e00660030_11];
        const __cope_m3_record_receiver_23 = point;
        __cope_m3_record_require_r1_10(__cope_m3_record_receiver_23);
        return __cope_m3_flow_value_3((__cope_m3_ordered_24 + __cope_m3_record_receiver_23[__cope_m3_record_field___cope_00720031002e00660031_12]));
        return __cope_m3_flow_value_3(undefined);
    })();
    __cope_m3_flow_validate_6(__cope_m3_function_flow_17);
    if (__cope_m3_function_flow_17.$kind === "value") {
        return __cope_m3_function_flow_17.$value;
    }
    if (__cope_m3_function_flow_17.$kind === "function") {
        __cope_m3_panic_0();
    }
    __cope_m3_panic_0();
}
