"use strict";

function __cope_async() {
    let terminal = 0;
    let value;
    const continuations = [];
    return Object.freeze({
        get completed() { return terminal !== 0; },
        get cancelled() { return terminal === 2; },
        get panicked() { return terminal === 3; },
        get transportFailed() { return terminal === 4; },
        get value() { return value; },
        subscribe(success, cancelled, failed, panicked) { if (terminal !== 0) return true; continuations.push({ success, cancelled, failed, panicked }); return false; },
        resolve(next) { if (terminal !== 0) return; terminal = 1; value = next; const pending = continuations.splice(0); for (const continuation of pending) continuation.success(); },
        cancel() { if (terminal !== 0) return; terminal = 2; const pending = continuations.splice(0); for (const continuation of pending) continuation.cancelled(); },
        fail() { if (terminal !== 0) return; terminal = 4; const pending = continuations.splice(0); for (const continuation of pending) continuation.failed(); },
        panic() { if (terminal !== 0) return; terminal = 3; const pending = continuations.splice(0); for (const continuation of pending) continuation.panicked(); },
    });
}
function __cope_async_pending() { return __cope_async(); }

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_make_1(type, tag, payload) {
    const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
    return value;
}

const __cope_m3_record_type_r1_2 = Symbol("r1");
const __cope_m3_record_instances_r1_3 = new WeakSet();
const __cope_m3_record_field___cope_00720031002e00660030_6 = Symbol("r1.f0");
const __cope_m3_record_field___cope_00720031002e00660031_7 = Symbol("r1.f1");

function __cope_m3_record_make_r1_4(field0, field1) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r1_2]: { value: __cope_m3_record_type_r1_2, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660030_6]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660031_7]: { value: field1, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r1_3.add(value);
    return value;
}

function __cope_m3_record_require_r1_5(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r1_3.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r1_2) || value[__cope_m3_record_type_r1_2] !== __cope_m3_record_type_r1_2 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660030_6) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660031_7)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_record_type_r2_8 = Symbol("r2");
const __cope_m3_record_instances_r2_9 = new WeakSet();
const __cope_m3_record_field___cope_00720032002e00660030_12 = Symbol("r2.f0");
const __cope_m3_record_field___cope_00720032002e00660031_13 = Symbol("r2.f1");

function __cope_m3_record_make_r2_10(field0, field1) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r2_8]: { value: __cope_m3_record_type_r2_8, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660030_12]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660031_13]: { value: field1, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r2_9.add(value);
    return value;
}

function __cope_m3_record_require_r2_11(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r2_9.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r2_8) || value[__cope_m3_record_type_r2_8] !== __cope_m3_record_type_r2_8 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660030_12) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660031_13)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_record_type_r3_14 = Symbol("r3");
const __cope_m3_record_instances_r3_15 = new WeakSet();
const __cope_m3_record_field___cope_00720033002e00660030_18 = Symbol("r3.f0");
const __cope_m3_record_field___cope_00720033002e00660031_19 = Symbol("r3.f1");

function __cope_m3_record_make_r3_16(field0, field1) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r3_14]: { value: __cope_m3_record_type_r3_14, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720033002e00660030_18]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720033002e00660031_19]: { value: field1, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r3_15.add(value);
    return value;
}

function __cope_m3_record_require_r3_17(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r3_15.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r3_14) || value[__cope_m3_record_type_r3_14] !== __cope_m3_record_type_r3_14 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720033002e00660030_18) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720033002e00660031_19)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_result_type_20 = Object.freeze(Object.create(null));

function __cope_m3_result_validate_21(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_20 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
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

function LoadedValue__constructor(raw, scaled) {
    const __cope_m3_record_init_22 = raw;
    const __cope_m3_record_init_23 = scaled;
    return __cope_m3_record_make_r2_10(__cope_m3_record_init_22, __cope_m3_record_init_23);
}

function LoadedValue__total(value) {
    const __cope_m3_record_receiver_24 = value;
    __cope_m3_record_require_r2_11(__cope_m3_record_receiver_24);
    const __cope_m3_ordered_26 = __cope_m3_record_receiver_24[__cope_m3_record_field___cope_00720032002e00660030_12];
    const __cope_m3_record_receiver_25 = value;
    __cope_m3_record_require_r2_11(__cope_m3_record_receiver_25);
    return (__cope_m3_ordered_26 + __cope_m3_record_receiver_25[__cope_m3_record_field___cope_00720032002e00660031_13]);
}

function normalize(value) {
    return (value * 2);
}

function validate(value) {
    if ((value < 0)) {
        return __cope_m3_make_1(__cope_m3_result_type_20, "err", ["negative"]);
    }
    return __cope_m3_make_1(__cope_m3_result_type_20, "ok", [value]);
}

function* seedValues() {
    yield 1;
    yield 2;
    yield 3;
}

function* values() {
    yield 0;
    yield* seedValues();
    yield 4;
}

function collect() {
    const buffer = (() => { const __cope_m3_array_length_27 = 5; if (__cope_m3_array_length_27 < 0) throw new RangeError("Copeland mutable array length cannot be negative."); return Array(__cope_m3_array_length_27).fill(0); })();
    let index = 0;
    for (const value of values()) {
        (() => { const __cope_m3_mutable_array_28 = buffer; const __cope_m3_array_index_29 = index; if (__cope_m3_array_index_29 < 0 || __cope_m3_array_index_29 >= __cope_m3_mutable_array_28.length) throw new RangeError("Copeland array index is out of bounds."); __cope_m3_mutable_array_28[__cope_m3_array_index_29] = value; return value; })();
        (index = (index + 1));
    }
    return Object.freeze(buffer.slice());
}

function transform(input) {
    const increment = 1;
    return (() => { const __cope_m3_batch_input_31 = input; const __cope_m3_batch_output_32 = new Array(__cope_m3_batch_input_31.length); for (let __cope_m3_batch_index_33 = 0; __cope_m3_batch_index_33 < __cope_m3_batch_input_31.length; __cope_m3_batch_index_33 += 1) { const value = __cope_m3_batch_input_31[__cope_m3_batch_index_33]; const __cope_m3_unwrap_30 = validate(value); __cope_m3_result_validate_21(__cope_m3_unwrap_30); if (__cope_m3_unwrap_30.$tag === "err") { (__cope_m3_unwrap_30.$payload[0]); } const checked = __cope_m3_unwrap_30.$payload[0]; __cope_m3_batch_output_32[__cope_m3_batch_index_33] = normalize((checked + increment)); } return __cope_m3_batch_output_32; })();
}

function load(value) {
    const frame = { __cope_state: 3 };
    frame.__parameter_value = value;
    const computation = __cope_async();
    function step() {
        if (computation.completed) return;
        while (true) {
            switch (frame.__cope_state) {
                case 0: {
                    computation.resolve(undefined);
                    return;
                }
                case 1: {
                    computation.resolve(__cope_m3_make_1(__cope_m3_result_type_20, "ok", [(frame.__local_checked + 1)]));
                    return;
                }
                case 2: {
                    frame.__local_checked = frame.__expression_0;
                    frame.__cope_state = 1;
                    continue;
                }
                case 3: {
                    const __cope_propagate_3 = validate(frame.__parameter_value);
                    if (__cope_propagate_3.$tag !== "ok") { computation.resolve(__cope_m3_make_1(__cope_m3_result_type_20, "err", [__cope_propagate_3.$payload[0]])); return; }
                    frame.__expression_0 = __cope_propagate_3.$payload[0];
                    frame.__cope_state = 2;
                    continue;
                }
                default: return;
            }
        }
    }
    step();
    return computation;
}

function compose(value) {
    const frame = { __cope_state: 7 };
    frame.__parameter_value = value;
    const computation = __cope_async();
    function step() {
        if (computation.completed) return;
        while (true) {
            switch (frame.__cope_state) {
                case 0: {
                    computation.resolve(undefined);
                    return;
                }
                case 1: {
                    computation.resolve(__cope_m3_make_1(__cope_m3_result_type_20, "ok", [((LoadedValue__total(frame.__local_boxed) + frame.__local_local[__cope_m3_record_field___cope_00720033002e00660030_18]) + frame.__local_local[__cope_m3_record_field___cope_00720033002e00660031_19])]));
                    return;
                }
                case 2: {
                    frame.__local_local = __cope_m3_record_make_r3_16(frame.__local_loaded, (frame.__local_loaded * 2));
                    frame.__cope_state = 1;
                    continue;
                }
                case 3: {
                    frame.__local_boxed = LoadedValue__constructor(frame.__local_loaded, (frame.__local_loaded * 2));
                    frame.__cope_state = 2;
                    continue;
                }
                case 4: {
                    frame.__local_loaded = frame.__expression_0;
                    frame.__cope_state = 3;
                    continue;
                }
                case 5: {
                    const __cope_propagate_5 = frame.__await_value_0;
                    if (__cope_propagate_5.$tag !== "ok") { computation.resolve(__cope_m3_make_1(__cope_m3_result_type_20, "err", [__cope_propagate_5.$payload[0]])); return; }
                    frame.__expression_0 = __cope_propagate_5.$payload[0];
                    frame.__cope_state = 4;
                    continue;
                }
                case 6: {
                    frame.__await_0 = frame.__local_pending;
                    frame.__cope_state = 5;
                    if (!frame.__await_0.subscribe(() => { frame.__await_value_0 = frame.__await_0.value; step(); }, () => computation.cancel(), () => computation.fail(), () => computation.panic())) return;
                    if (frame.__await_0.cancelled) { computation.cancel(); return; }
                    if (frame.__await_0.transportFailed) { computation.fail(); return; }
                    if (frame.__await_0.panicked) { computation.panic(); return; }
                    frame.__await_value_0 = frame.__await_0.value;
                    continue;
                }
                case 7: {
                    frame.__local_pending = load(frame.__parameter_value);
                    frame.__cope_state = 6;
                    continue;
                }
                default: return;
            }
        }
    }
    step();
    return computation;
}

function main() {
    const collected = collect();
    const transformed = transform(collected);
    let total = 0;
    for (const value of transformed) {
        (total = (total + value));
    }
    return total;
}
