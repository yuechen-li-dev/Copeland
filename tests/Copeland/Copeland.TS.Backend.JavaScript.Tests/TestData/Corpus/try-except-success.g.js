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

const __cope_m3_result_type_7 = Object.freeze(Object.create(null));

function __cope_m3_result_validate_8(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_7 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!(typeof value.$payload[0] === "number")) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function read() {
    return __cope_m3_make_1(__cope_m3_result_type_7, "ok", [4]);
}

function main() {
    const __cope_m3_function_flow_9 = (() => {
        const __cope_m3_try_protected_10 = (() => {
            const __cope_m3_propagate_13 = read();
            __cope_m3_result_validate_8(__cope_m3_propagate_13);
            if (__cope_m3_propagate_13.$tag === "err") {
                return __cope_m3_flow_handler_4(1, __cope_m3_propagate_13.$payload[0]);
            }
            const value = __cope_m3_propagate_13.$payload[0];
            return __cope_m3_flow_value_3((value + 1));
        })();
        __cope_m3_flow_validate_6(__cope_m3_try_protected_10);
        let __cope_m3_try_value_12;
        if (__cope_m3_try_protected_10.$kind === "handler" && __cope_m3_try_protected_10.$handler === 1) {
            const error = __cope_m3_try_protected_10.$error;
            const __cope_m3_try_handler_11 = (() => {
                return __cope_m3_flow_value_3(0);
            })();
            __cope_m3_flow_validate_6(__cope_m3_try_handler_11);
            if (__cope_m3_try_handler_11.$kind !== "value") {
                return __cope_m3_try_handler_11;
            }
            __cope_m3_try_value_12 = __cope_m3_try_handler_11.$value;
        } else if (__cope_m3_try_protected_10.$kind === "value") {
            __cope_m3_try_value_12 = __cope_m3_try_protected_10.$value;
        } else {
            return __cope_m3_try_protected_10;
        }
        return __cope_m3_flow_value_3(__cope_m3_try_value_12);
        return __cope_m3_flow_value_3(undefined);
    })();
    __cope_m3_flow_validate_6(__cope_m3_function_flow_9);
    if (__cope_m3_function_flow_9.$kind === "value") {
        return __cope_m3_function_flow_9.$value;
    }
    if (__cope_m3_function_flow_9.$kind === "function") {
        __cope_m3_panic_0();
    }
    __cope_m3_panic_0();
}
