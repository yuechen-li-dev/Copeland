"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_make_1(type, tag, payload) {
    return Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
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
const __cope_m3_record_field___cope_00720031002e00660030_10 = Symbol("r1.f0");
const __cope_m3_record_field___cope_00720031002e00660031_11 = Symbol("r1.f1");

function __cope_m3_record_make_r1_8(field0, field1) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r1_7]: { value: __cope_m3_record_type_r1_7, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660030_10]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660031_11]: { value: field1, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(value);
}

function __cope_m3_record_require_r1_9(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r1_7) || value[__cope_m3_record_type_r1_7] !== __cope_m3_record_type_r1_7 || Object.getOwnPropertySymbols(value).length !== 3 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660030_10) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660031_11)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_result_type_12 = Object.freeze(Object.create(null));

function __cope_m3_result_validate_13(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_12 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_record_require_r1_9(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function bad() {
    return __cope_m3_make_1(__cope_m3_result_type_12, "err", ["bad"]);
}

function fallback() {
    const __cope_m3_record_init_14 = 40;
    const __cope_m3_record_init_15 = 2;
    return __cope_m3_record_make_r1_8(__cope_m3_record_init_14, __cope_m3_record_init_15);
}

function main() {
    const __cope_m3_function_flow_16 = (() => {
        const __cope_m3_try_protected_17 = (() => {
            const __cope_m3_propagate_20 = bad();
            __cope_m3_result_validate_13(__cope_m3_propagate_20);
            if (__cope_m3_propagate_20.$tag === "err") {
                return __cope_m3_flow_handler_4(1, __cope_m3_propagate_20.$payload[0]);
            }
            return __cope_m3_flow_value_3(__cope_m3_propagate_20.$payload[0]);
        })();
        __cope_m3_flow_validate_6(__cope_m3_try_protected_17);
        let __cope_m3_try_value_19;
        if (__cope_m3_try_protected_17.$kind === "handler" && __cope_m3_try_protected_17.$handler === 1) {
            const error = __cope_m3_try_protected_17.$error;
            const __cope_m3_try_handler_18 = (() => {
                return __cope_m3_flow_value_3(fallback());
            })();
            __cope_m3_flow_validate_6(__cope_m3_try_handler_18);
            if (__cope_m3_try_handler_18.$kind !== "value") {
                return __cope_m3_try_handler_18;
            }
            __cope_m3_try_value_19 = __cope_m3_try_handler_18.$value;
        } else if (__cope_m3_try_protected_17.$kind === "value") {
            __cope_m3_try_value_19 = __cope_m3_try_protected_17.$value;
        } else {
            return __cope_m3_try_protected_17;
        }
        const point = __cope_m3_try_value_19;
        const __cope_m3_record_receiver_21 = point;
        __cope_m3_record_require_r1_9(__cope_m3_record_receiver_21);
        const __cope_m3_ordered_23 = __cope_m3_record_receiver_21[__cope_m3_record_field___cope_00720031002e00660030_10];
        const __cope_m3_record_receiver_22 = point;
        __cope_m3_record_require_r1_9(__cope_m3_record_receiver_22);
        return __cope_m3_flow_value_3((__cope_m3_ordered_23 + __cope_m3_record_receiver_22[__cope_m3_record_field___cope_00720031002e00660031_11]));
        return __cope_m3_flow_value_3(undefined);
    })();
    __cope_m3_flow_validate_6(__cope_m3_function_flow_16);
    if (__cope_m3_function_flow_16.$kind === "value") {
        return __cope_m3_function_flow_16.$value;
    }
    if (__cope_m3_function_flow_16.$kind === "function") {
        __cope_m3_panic_0();
    }
    __cope_m3_panic_0();
}
