"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_panic_unwrap_1(error) {
    const panic = new Error("COPE-PANIC-UNWRAP: Result unwrap encountered err");
    panic.error = error;
    throw panic;
}

function __cope_m3_make_2(type, tag, payload) {
    const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
    if (type === __cope_m3_type_3) __cope_m3_instances_4.add(value);
    return value;
}

const __cope_m3_type_3 = Object.freeze(Object.create(null));
const __cope_m3_instances_4 = new WeakSet();

function __cope_m3_validate_5(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_4.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_3 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "InvalidIndex":
            if (value.$payload.length !== 1) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "number")) {
                __cope_m3_panic_0();
            }
            return;
        case "OutOfBounds":
            if (value.$payload.length !== 2) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "number")) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 1) || !(typeof value.$payload[1] === "number")) {
                __cope_m3_panic_0();
            }
            return;
        default:
            __cope_m3_panic_0();
    }
}

const __cope_m3_result_type_6 = Object.freeze(Object.create(null));

const __cope_m3_result_type_8 = Object.freeze(Object.create(null));

const __cope_m3_result_type_10 = Object.freeze(Object.create(null));

const __cope_m3_result_type_12 = Object.freeze(Object.create(null));

function __cope_m3_result_validate_7(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_6 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!(typeof value.$payload[0] === "number")) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_5(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_9(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_8 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_table_row_require_t1_22(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_5(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_11(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_10 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_5(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_13(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_12 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_table_row_require_t2_34(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_5(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

const __cope_m3_column_type_14 = Symbol("cope.column");
const __cope_m3_column_read_15 = Symbol("cope.column.read");
const __cope_m3_table_row_table_17 = Symbol("cope.table.row.table");
const __cope_m3_table_row_index_18 = Symbol("cope.table.row.index");

function __cope_m3_column_require_16(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_column_type_14) || value[__cope_m3_column_type_14] !== __cope_m3_column_type_14 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_column_read_15) || typeof value[__cope_m3_column_read_15] !== "function" || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_table_type_t1_19 = Symbol("t1");
const __cope_m3_table_row_type_t1_20 = Symbol("t1.row");
const __cope_m3_table_rows_t1_26 = Symbol("t1.rows");
const __cope_m3_table_column___cope_00740031002e00630030_27 = Symbol("t1.c0");
const __cope_m3_column_type___cope_00740031002e00630030_28 = Symbol("t1.c0.column");

function __cope_m3_table_require_t1_21(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_type_t1_19) || value[__cope_m3_table_type_t1_19] !== __cope_m3_table_type_t1_19 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_rows_t1_26) || typeof value[__cope_m3_table_rows_t1_26] !== "function" || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630030_27)) { __cope_m3_panic_0(); }
}

function __cope_m3_table_row_require_t1_22(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_type_t1_20) || value[__cope_m3_table_row_type_t1_20] !== __cope_m3_table_row_type_t1_20 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_table_17) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_index_18) || !Number.isInteger(value[__cope_m3_table_row_index_18]) || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
    __cope_m3_table_require_t1_21(value[__cope_m3_table_row_table_17]);
}

function __cope_m3_table_row_create_t1_24(tableValue, index) {
    const row = Object.create(null);
    Object.defineProperties(row, {
        [__cope_m3_table_row_type_t1_20]: { value: __cope_m3_table_row_type_t1_20, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_table_17]: { value: tableValue, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_index_18]: { value: index, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(row);
}

function __cope_m3_table_create_t1_23() {
    const __cope_m3_table_storage___cope_00740031002e00630030_29 = Object.freeze([]);
    const __cope_m3_table_column_value___cope_00740031002e00630030_30 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630030_30, {
        [__cope_m3_column_type_14]: { value: __cope_m3_column_type_14, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630030_28]: { value: __cope_m3_column_type___cope_00740031002e00630030_28, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_15]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_6, "err", [__cope_m3_make_2(__cope_m3_type_3, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 0) {
                return __cope_m3_make_2(__cope_m3_result_type_6, "err", [__cope_m3_make_2(__cope_m3_type_3, "OutOfBounds", [index, 0])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_6, "ok", [__cope_m3_table_storage___cope_00740031002e00630030_29[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630030_30);
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_table_type_t1_19]: { value: __cope_m3_table_type_t1_19, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_rows_t1_26]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_8, "err", [__cope_m3_make_2(__cope_m3_type_3, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 0) {
                return __cope_m3_make_2(__cope_m3_result_type_8, "err", [__cope_m3_make_2(__cope_m3_type_3, "OutOfBounds", [index, 0])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_8, "ok", [__cope_m3_table_row_create_t1_24(value, index)]);
        }, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630030_27]: { value: __cope_m3_table_column_value___cope_00740031002e00630030_30, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(value);
}

const __cope_m3_table_value_t1_25 = __cope_m3_table_create_t1_23();

const __cope_m3_table_type_t2_31 = Symbol("t2");
const __cope_m3_table_row_type_t2_32 = Symbol("t2.row");
const __cope_m3_table_rows_t2_38 = Symbol("t2.rows");
const __cope_m3_table_column___cope_00740032002e00630030_39 = Symbol("t2.c0");
const __cope_m3_column_type___cope_00740032002e00630030_40 = Symbol("t2.c0.column");
const __cope_m3_table_column___cope_00740032002e00630031_43 = Symbol("t2.c1");
const __cope_m3_column_type___cope_00740032002e00630031_44 = Symbol("t2.c1.column");

function __cope_m3_table_require_t2_33(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_type_t2_31) || value[__cope_m3_table_type_t2_31] !== __cope_m3_table_type_t2_31 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_rows_t2_38) || typeof value[__cope_m3_table_rows_t2_38] !== "function" || Object.getOwnPropertySymbols(value).length !== 4) {
        __cope_m3_panic_0();
    }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740032002e00630030_39)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740032002e00630031_43)) { __cope_m3_panic_0(); }
}

function __cope_m3_table_row_require_t2_34(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_type_t2_32) || value[__cope_m3_table_row_type_t2_32] !== __cope_m3_table_row_type_t2_32 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_table_17) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_index_18) || !Number.isInteger(value[__cope_m3_table_row_index_18]) || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
    __cope_m3_table_require_t2_33(value[__cope_m3_table_row_table_17]);
}

function __cope_m3_table_row_create_t2_36(tableValue, index) {
    const row = Object.create(null);
    Object.defineProperties(row, {
        [__cope_m3_table_row_type_t2_32]: { value: __cope_m3_table_row_type_t2_32, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_table_17]: { value: tableValue, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_index_18]: { value: index, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(row);
}

function __cope_m3_table_create_t2_35() {
    const __cope_m3_table_storage___cope_00740032002e00630030_41 = Object.freeze([-0, 2]);
    const __cope_m3_table_column_value___cope_00740032002e00630030_42 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740032002e00630030_42, {
        [__cope_m3_column_type_14]: { value: __cope_m3_column_type_14, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740032002e00630030_40]: { value: __cope_m3_column_type___cope_00740032002e00630030_40, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_15]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_6, "err", [__cope_m3_make_2(__cope_m3_type_3, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_6, "err", [__cope_m3_make_2(__cope_m3_type_3, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_6, "ok", [__cope_m3_table_storage___cope_00740032002e00630030_41[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740032002e00630030_42);
    const __cope_m3_table_storage___cope_00740032002e00630031_45 = Object.freeze(["zero", "two"]);
    const __cope_m3_table_column_value___cope_00740032002e00630031_46 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740032002e00630031_46, {
        [__cope_m3_column_type_14]: { value: __cope_m3_column_type_14, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740032002e00630031_44]: { value: __cope_m3_column_type___cope_00740032002e00630031_44, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_15]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_10, "err", [__cope_m3_make_2(__cope_m3_type_3, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_10, "err", [__cope_m3_make_2(__cope_m3_type_3, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_10, "ok", [__cope_m3_table_storage___cope_00740032002e00630031_45[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740032002e00630031_46);
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_table_type_t2_31]: { value: __cope_m3_table_type_t2_31, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_rows_t2_38]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_12, "err", [__cope_m3_make_2(__cope_m3_type_3, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_12, "err", [__cope_m3_make_2(__cope_m3_type_3, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_12, "ok", [__cope_m3_table_row_create_t2_36(value, index)]);
        }, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740032002e00630030_39]: { value: __cope_m3_table_column_value___cope_00740032002e00630030_42, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740032002e00630031_43]: { value: __cope_m3_table_column_value___cope_00740032002e00630031_46, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(value);
}

const __cope_m3_table_value_t2_37 = __cope_m3_table_create_t2_35();

function main() {
    const __cope_m3_table_receiver_47 = __cope_m3_table_value_t2_37;
    const __cope_m3_table_index_48 = 1;
    __cope_m3_table_require_t2_33(__cope_m3_table_receiver_47);
    const __cope_m3_table_row_49 = __cope_m3_table_receiver_47[__cope_m3_table_rows_t2_38](__cope_m3_table_index_48);
    __cope_m3_result_validate_13(__cope_m3_table_row_49);
    const __cope_m3_unwrap_50 = __cope_m3_table_row_49;
    __cope_m3_result_validate_13(__cope_m3_unwrap_50);
    if (__cope_m3_unwrap_50.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_50.$payload[0]);
    }
    const row = __cope_m3_unwrap_50.$payload[0];
    const __cope_m3_table_row_51 = row;
    __cope_m3_table_row_require_t2_34(__cope_m3_table_row_51);
    const __cope_m3_row_table_52 = __cope_m3_table_row_51[__cope_m3_table_row_table_17];
    const __cope_m3_row_field_53 = __cope_m3_row_table_52[__cope_m3_table_column___cope_00740032002e00630030_39][__cope_m3_column_read_15](__cope_m3_table_row_51[__cope_m3_table_row_index_18]);
    __cope_m3_result_validate_7(__cope_m3_row_field_53);
    if (__cope_m3_row_field_53.$tag !== "ok") { __cope_m3_panic_0(); }
    return __cope_m3_row_field_53.$payload[0];
}

function bounds(index) {
    const __cope_m3_table_receiver_54 = __cope_m3_table_value_t2_37;
    __cope_m3_table_require_t2_33(__cope_m3_table_receiver_54);
    const __cope_m3_column_receiver_55 = __cope_m3_table_receiver_54[__cope_m3_table_column___cope_00740032002e00630030_39];
    const __cope_m3_column_index_56 = index;
    __cope_m3_column_require_16(__cope_m3_column_receiver_55);
    const __cope_m3_column_element_57 = __cope_m3_column_receiver_55[__cope_m3_column_read_15](__cope_m3_column_index_56);
    __cope_m3_result_validate_7(__cope_m3_column_element_57);
    const __cope_m3_result_match_59 = __cope_m3_column_element_57;
    __cope_m3_result_validate_7(__cope_m3_result_match_59);
    let __cope_m3_result_value_60;
    switch (__cope_m3_result_match_59.$tag) {
        case "ok": {
            const value = __cope_m3_result_match_59.$payload[0];
            __cope_m3_result_value_60 = value;
            break;
        }
        case "err": {
            const error = __cope_m3_result_match_59.$payload[0];
            __cope_m3_result_value_60 = (() => { const __cope_m3_match_58 = error; __cope_m3_validate_5(__cope_m3_match_58); switch (__cope_m3_match_58.$tag) { case "InvalidIndex": { const value = __cope_m3_match_58.$payload[0]; return 10; } case "OutOfBounds": { const value = __cope_m3_match_58.$payload[0]; const rowCount = __cope_m3_match_58.$payload[1]; return rowCount; } default: return __cope_m3_panic_0(); } })();
            break;
        }
        default:
            __cope_m3_panic_0();
    }
    return __cope_m3_result_value_60;
}
