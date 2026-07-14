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
    return Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
}

const __cope_m3_type_3 = Object.freeze(Object.create(null));

function __cope_m3_validate_4(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_3 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
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

const __cope_m3_result_type_5 = Object.freeze(Object.create(null));

const __cope_m3_result_type_7 = Object.freeze(Object.create(null));

const __cope_m3_result_type_9 = Object.freeze(Object.create(null));

const __cope_m3_result_type_11 = Object.freeze(Object.create(null));

function __cope_m3_result_validate_6(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_5 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!(typeof value.$payload[0] === "number")) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_4(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_8(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_7 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_table_row_require_t1_21(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_4(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_10(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_9 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_4(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_12(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_11 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_table_row_require_t2_33(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_4(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

const __cope_m3_column_type_13 = Symbol("cope.column");
const __cope_m3_column_read_14 = Symbol("cope.column.read");
const __cope_m3_table_row_table_16 = Symbol("cope.table.row.table");
const __cope_m3_table_row_index_17 = Symbol("cope.table.row.index");

function __cope_m3_column_require_15(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_column_type_13) || value[__cope_m3_column_type_13] !== __cope_m3_column_type_13 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_column_read_14) || typeof value[__cope_m3_column_read_14] !== "function" || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_table_type_t1_18 = Symbol("t1");
const __cope_m3_table_row_type_t1_19 = Symbol("t1.row");
const __cope_m3_table_rows_t1_25 = Symbol("t1.rows");
const __cope_m3_table_column___cope_00740031002e00630030_26 = Symbol("t1.c0");
const __cope_m3_column_type___cope_00740031002e00630030_27 = Symbol("t1.c0.column");

function __cope_m3_table_require_t1_20(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_type_t1_18) || value[__cope_m3_table_type_t1_18] !== __cope_m3_table_type_t1_18 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_rows_t1_25) || typeof value[__cope_m3_table_rows_t1_25] !== "function" || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630030_26)) { __cope_m3_panic_0(); }
}

function __cope_m3_table_row_require_t1_21(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_type_t1_19) || value[__cope_m3_table_row_type_t1_19] !== __cope_m3_table_row_type_t1_19 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_table_16) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_index_17) || !Number.isInteger(value[__cope_m3_table_row_index_17]) || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
    __cope_m3_table_require_t1_20(value[__cope_m3_table_row_table_16]);
}

function __cope_m3_table_row_create_t1_23(tableValue, index) {
    const row = Object.create(null);
    Object.defineProperties(row, {
        [__cope_m3_table_row_type_t1_19]: { value: __cope_m3_table_row_type_t1_19, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_table_16]: { value: tableValue, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_index_17]: { value: index, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(row);
}

function __cope_m3_table_create_t1_22() {
    const __cope_m3_table_storage___cope_00740031002e00630030_28 = Object.freeze([]);
    const __cope_m3_table_column_value___cope_00740031002e00630030_29 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630030_29, {
        [__cope_m3_column_type_13]: { value: __cope_m3_column_type_13, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630030_27]: { value: __cope_m3_column_type___cope_00740031002e00630030_27, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_14]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_5, "err", [__cope_m3_make_2(__cope_m3_type_3, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 0) {
                return __cope_m3_make_2(__cope_m3_result_type_5, "err", [__cope_m3_make_2(__cope_m3_type_3, "OutOfBounds", [index, 0])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_5, "ok", [__cope_m3_table_storage___cope_00740031002e00630030_28[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630030_29);
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_table_type_t1_18]: { value: __cope_m3_table_type_t1_18, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_rows_t1_25]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_7, "err", [__cope_m3_make_2(__cope_m3_type_3, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 0) {
                return __cope_m3_make_2(__cope_m3_result_type_7, "err", [__cope_m3_make_2(__cope_m3_type_3, "OutOfBounds", [index, 0])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_7, "ok", [__cope_m3_table_row_create_t1_23(value, index)]);
        }, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630030_26]: { value: __cope_m3_table_column_value___cope_00740031002e00630030_29, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(value);
}

const __cope_m3_table_value_t1_24 = __cope_m3_table_create_t1_22();

const __cope_m3_table_type_t2_30 = Symbol("t2");
const __cope_m3_table_row_type_t2_31 = Symbol("t2.row");
const __cope_m3_table_rows_t2_37 = Symbol("t2.rows");
const __cope_m3_table_column___cope_00740032002e00630030_38 = Symbol("t2.c0");
const __cope_m3_column_type___cope_00740032002e00630030_39 = Symbol("t2.c0.column");
const __cope_m3_table_column___cope_00740032002e00630031_42 = Symbol("t2.c1");
const __cope_m3_column_type___cope_00740032002e00630031_43 = Symbol("t2.c1.column");

function __cope_m3_table_require_t2_32(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_type_t2_30) || value[__cope_m3_table_type_t2_30] !== __cope_m3_table_type_t2_30 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_rows_t2_37) || typeof value[__cope_m3_table_rows_t2_37] !== "function" || Object.getOwnPropertySymbols(value).length !== 4) {
        __cope_m3_panic_0();
    }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740032002e00630030_38)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740032002e00630031_42)) { __cope_m3_panic_0(); }
}

function __cope_m3_table_row_require_t2_33(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_type_t2_31) || value[__cope_m3_table_row_type_t2_31] !== __cope_m3_table_row_type_t2_31 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_table_16) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_index_17) || !Number.isInteger(value[__cope_m3_table_row_index_17]) || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
    __cope_m3_table_require_t2_32(value[__cope_m3_table_row_table_16]);
}

function __cope_m3_table_row_create_t2_35(tableValue, index) {
    const row = Object.create(null);
    Object.defineProperties(row, {
        [__cope_m3_table_row_type_t2_31]: { value: __cope_m3_table_row_type_t2_31, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_table_16]: { value: tableValue, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_index_17]: { value: index, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(row);
}

function __cope_m3_table_create_t2_34() {
    const __cope_m3_table_storage___cope_00740032002e00630030_40 = Object.freeze([-0, 2]);
    const __cope_m3_table_column_value___cope_00740032002e00630030_41 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740032002e00630030_41, {
        [__cope_m3_column_type_13]: { value: __cope_m3_column_type_13, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740032002e00630030_39]: { value: __cope_m3_column_type___cope_00740032002e00630030_39, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_14]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_5, "err", [__cope_m3_make_2(__cope_m3_type_3, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_5, "err", [__cope_m3_make_2(__cope_m3_type_3, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_5, "ok", [__cope_m3_table_storage___cope_00740032002e00630030_40[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740032002e00630030_41);
    const __cope_m3_table_storage___cope_00740032002e00630031_44 = Object.freeze(["zero", "two"]);
    const __cope_m3_table_column_value___cope_00740032002e00630031_45 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740032002e00630031_45, {
        [__cope_m3_column_type_13]: { value: __cope_m3_column_type_13, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740032002e00630031_43]: { value: __cope_m3_column_type___cope_00740032002e00630031_43, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_14]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_9, "err", [__cope_m3_make_2(__cope_m3_type_3, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_9, "err", [__cope_m3_make_2(__cope_m3_type_3, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_9, "ok", [__cope_m3_table_storage___cope_00740032002e00630031_44[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740032002e00630031_45);
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_table_type_t2_30]: { value: __cope_m3_table_type_t2_30, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_rows_t2_37]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_11, "err", [__cope_m3_make_2(__cope_m3_type_3, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_11, "err", [__cope_m3_make_2(__cope_m3_type_3, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_11, "ok", [__cope_m3_table_row_create_t2_35(value, index)]);
        }, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740032002e00630030_38]: { value: __cope_m3_table_column_value___cope_00740032002e00630030_41, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740032002e00630031_42]: { value: __cope_m3_table_column_value___cope_00740032002e00630031_45, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(value);
}

const __cope_m3_table_value_t2_36 = __cope_m3_table_create_t2_34();

function main() {
    const __cope_m3_table_receiver_46 = __cope_m3_table_value_t2_36;
    const __cope_m3_table_index_47 = 1;
    __cope_m3_table_require_t2_32(__cope_m3_table_receiver_46);
    const __cope_m3_table_row_48 = __cope_m3_table_receiver_46[__cope_m3_table_rows_t2_37](__cope_m3_table_index_47);
    __cope_m3_result_validate_12(__cope_m3_table_row_48);
    const __cope_m3_unwrap_49 = __cope_m3_table_row_48;
    __cope_m3_result_validate_12(__cope_m3_unwrap_49);
    if (__cope_m3_unwrap_49.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_49.$payload[0]);
    }
    const row = __cope_m3_unwrap_49.$payload[0];
    const __cope_m3_table_row_50 = row;
    __cope_m3_table_row_require_t2_33(__cope_m3_table_row_50);
    const __cope_m3_row_table_51 = __cope_m3_table_row_50[__cope_m3_table_row_table_16];
    const __cope_m3_row_field_52 = __cope_m3_row_table_51[__cope_m3_table_column___cope_00740032002e00630030_38][__cope_m3_column_read_14](__cope_m3_table_row_50[__cope_m3_table_row_index_17]);
    __cope_m3_result_validate_6(__cope_m3_row_field_52);
    if (__cope_m3_row_field_52.$tag !== "ok") { __cope_m3_panic_0(); }
    return __cope_m3_row_field_52.$payload[0];
}

function bounds(index) {
    const __cope_m3_table_receiver_53 = __cope_m3_table_value_t2_36;
    __cope_m3_table_require_t2_32(__cope_m3_table_receiver_53);
    const __cope_m3_column_receiver_54 = __cope_m3_table_receiver_53[__cope_m3_table_column___cope_00740032002e00630030_38];
    const __cope_m3_column_index_55 = index;
    __cope_m3_column_require_15(__cope_m3_column_receiver_54);
    const __cope_m3_column_element_56 = __cope_m3_column_receiver_54[__cope_m3_column_read_14](__cope_m3_column_index_55);
    __cope_m3_result_validate_6(__cope_m3_column_element_56);
    const __cope_m3_result_match_58 = __cope_m3_column_element_56;
    __cope_m3_result_validate_6(__cope_m3_result_match_58);
    let __cope_m3_result_value_59;
    switch (__cope_m3_result_match_58.$tag) {
        case "ok": {
            const value = __cope_m3_result_match_58.$payload[0];
            __cope_m3_result_value_59 = value;
            break;
        }
        case "err": {
            const error = __cope_m3_result_match_58.$payload[0];
            __cope_m3_result_value_59 = (() => { const __cope_m3_match_57 = error; __cope_m3_validate_4(__cope_m3_match_57); switch (__cope_m3_match_57.$tag) { case "InvalidIndex": { const value = __cope_m3_match_57.$payload[0]; return 10; } case "OutOfBounds": { const value = __cope_m3_match_57.$payload[0]; const rowCount = __cope_m3_match_57.$payload[1]; return rowCount; } default: return __cope_m3_panic_0(); } })();
            break;
        }
        default:
            __cope_m3_panic_0();
    }
    return __cope_m3_result_value_59;
}
