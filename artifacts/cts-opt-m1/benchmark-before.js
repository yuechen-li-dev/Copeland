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
    return value;
}

const __cope_m3_type_3 = Symbol("TableBoundsError");

function __cope_m3_validate_5(value) {
    if (typeof value !== "object" || value === null || value.$type !== __cope_m3_type_3 || typeof value.$tag !== "string") {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "InvalidIndex":
            if (Object.keys(value).length !== 3 || Object.getOwnPropertySymbols(value).length !== 0) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value, "$p0") || !(typeof value.$p0 === "number")) {
                __cope_m3_panic_0();
            }
            return;
        case "OutOfBounds":
            if (Object.keys(value).length !== 4 || Object.getOwnPropertySymbols(value).length !== 0) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value, "$p0") || !(typeof value.$p0 === "number")) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value, "$p1") || !(typeof value.$p1 === "number")) {
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
            if (!(typeof value.$payload[0] === "number")) { __cope_m3_panic_0(); }
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
            if (!((__cope_m3_table_row_require_t1_20(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_5(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

const __cope_m3_column_type_12 = Symbol("cope.column");
const __cope_m3_column_read_13 = Symbol("cope.column.read");
const __cope_m3_table_row_table_15 = Symbol("cope.table.row.table");
const __cope_m3_table_row_index_16 = Symbol("cope.table.row.index");

function __cope_m3_column_require_14(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_column_type_12) || value[__cope_m3_column_type_12] !== __cope_m3_column_type_12 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_column_read_13) || typeof value[__cope_m3_column_read_13] !== "function" || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_table_type_t1_17 = Symbol("t1");
const __cope_m3_table_row_type_t1_18 = Symbol("t1.row");
const __cope_m3_table_rows_t1_24 = Symbol("t1.rows");
const __cope_m3_table_column___cope_00740031002e00630030_25 = Symbol("t1.c0");
const __cope_m3_column_type___cope_00740031002e00630030_26 = Symbol("t1.c0.column");
const __cope_m3_table_column___cope_00740031002e00630031_29 = Symbol("t1.c1");
const __cope_m3_column_type___cope_00740031002e00630031_30 = Symbol("t1.c1.column");

function __cope_m3_table_require_t1_19(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_type_t1_17) || value[__cope_m3_table_type_t1_17] !== __cope_m3_table_type_t1_17 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_rows_t1_24) || typeof value[__cope_m3_table_rows_t1_24] !== "function" || Object.getOwnPropertySymbols(value).length !== 4) {
        __cope_m3_panic_0();
    }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630030_25)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630031_29)) { __cope_m3_panic_0(); }
}

function __cope_m3_table_row_require_t1_20(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_type_t1_18) || value[__cope_m3_table_row_type_t1_18] !== __cope_m3_table_row_type_t1_18 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_table_15) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_index_16) || !Number.isInteger(value[__cope_m3_table_row_index_16]) || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
    __cope_m3_table_require_t1_19(value[__cope_m3_table_row_table_15]);
}

function __cope_m3_table_row_create_t1_22(tableValue, index) {
    const row = Object.create(null);
    Object.defineProperties(row, {
        [__cope_m3_table_row_type_t1_18]: { value: __cope_m3_table_row_type_t1_18, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_table_15]: { value: tableValue, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_index_16]: { value: index, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(row);
}

function __cope_m3_table_create_t1_21() {
    const __cope_m3_table_storage___cope_00740031002e00630030_27 = Object.freeze([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
    const __cope_m3_table_column_value___cope_00740031002e00630030_28 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630030_28, {
        [__cope_m3_column_type_12]: { value: __cope_m3_column_type_12, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630030_26]: { value: __cope_m3_column_type___cope_00740031002e00630030_26, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_13]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_6, "err", [{ $type: __cope_m3_type_3, $tag: "InvalidIndex", $p0: index }]);
            }
            if (index < 0 || index >= 10) {
                return __cope_m3_make_2(__cope_m3_result_type_6, "err", [{ $type: __cope_m3_type_3, $tag: "OutOfBounds", $p0: index, $p1: 10 }]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_6, "ok", [__cope_m3_table_storage___cope_00740031002e00630030_27[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630030_28);
    const __cope_m3_table_storage___cope_00740031002e00630031_31 = Object.freeze([0.5, 1.5, 2.5, 3.5, 4.5, 5.5, 6.5, 7.5, 8.5, 9.5]);
    const __cope_m3_table_column_value___cope_00740031002e00630031_32 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630031_32, {
        [__cope_m3_column_type_12]: { value: __cope_m3_column_type_12, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630031_30]: { value: __cope_m3_column_type___cope_00740031002e00630031_30, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_13]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_8, "err", [{ $type: __cope_m3_type_3, $tag: "InvalidIndex", $p0: index }]);
            }
            if (index < 0 || index >= 10) {
                return __cope_m3_make_2(__cope_m3_result_type_8, "err", [{ $type: __cope_m3_type_3, $tag: "OutOfBounds", $p0: index, $p1: 10 }]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_8, "ok", [__cope_m3_table_storage___cope_00740031002e00630031_31[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630031_32);
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_table_type_t1_17]: { value: __cope_m3_table_type_t1_17, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_rows_t1_24]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_10, "err", [{ $type: __cope_m3_type_3, $tag: "InvalidIndex", $p0: index }]);
            }
            if (index < 0 || index >= 10) {
                return __cope_m3_make_2(__cope_m3_result_type_10, "err", [{ $type: __cope_m3_type_3, $tag: "OutOfBounds", $p0: index, $p1: 10 }]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_10, "ok", [__cope_m3_table_row_create_t1_22(value, index)]);
        }, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630030_25]: { value: __cope_m3_table_column_value___cope_00740031002e00630030_28, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630031_29]: { value: __cope_m3_table_column_value___cope_00740031002e00630031_32, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(value);
}

const __cope_m3_table_value_t1_23 = __cope_m3_table_create_t1_21();

function rowAccess(iterations) {
    let index = 0;
    let total = 0;
    while ((index < iterations)) {
        const __cope_m3_table_receiver_33 = __cope_m3_table_value_t1_23;
        const __cope_m3_table_index_34 = (index % 10);
        __cope_m3_table_require_t1_19(__cope_m3_table_receiver_33);
        const __cope_m3_table_row_35 = __cope_m3_table_receiver_33[__cope_m3_table_rows_t1_24](__cope_m3_table_index_34);
        __cope_m3_result_validate_11(__cope_m3_table_row_35);
        const __cope_m3_unwrap_36 = __cope_m3_table_row_35;
        __cope_m3_result_validate_11(__cope_m3_unwrap_36);
        if (__cope_m3_unwrap_36.$tag === "err") {
            __cope_m3_panic_unwrap_1(__cope_m3_unwrap_36.$payload[0]);
        }
        const row = __cope_m3_unwrap_36.$payload[0];
        const __cope_m3_ordered_40 = total;
        const __cope_m3_table_row_37 = row;
        __cope_m3_table_row_require_t1_20(__cope_m3_table_row_37);
        const __cope_m3_row_table_38 = __cope_m3_table_row_37[__cope_m3_table_row_table_15];
        const __cope_m3_row_field_39 = __cope_m3_row_table_38[__cope_m3_table_column___cope_00740031002e00630030_25][__cope_m3_column_read_13](__cope_m3_table_row_37[__cope_m3_table_row_index_16]);
        __cope_m3_result_validate_7(__cope_m3_row_field_39);
        if (__cope_m3_row_field_39.$tag !== "ok") { __cope_m3_panic_0(); }
        (total = (__cope_m3_ordered_40 + __cope_m3_row_field_39.$payload[0]));
        (index = (index + 1));
    }
    return total;
}

function columnAccess(iterations) {
    let index = 0;
    let total = 0;
    while ((index < iterations)) {
        const __cope_m3_table_receiver_41 = __cope_m3_table_value_t1_23;
        __cope_m3_table_require_t1_19(__cope_m3_table_receiver_41);
        const values = __cope_m3_table_receiver_41[__cope_m3_table_column___cope_00740031002e00630030_25];
        const __cope_m3_ordered_46 = total;
        const __cope_m3_column_receiver_42 = values;
        const __cope_m3_column_index_43 = 0;
        __cope_m3_column_require_14(__cope_m3_column_receiver_42);
        const __cope_m3_column_element_44 = __cope_m3_column_receiver_42[__cope_m3_column_read_13](__cope_m3_column_index_43);
        __cope_m3_result_validate_7(__cope_m3_column_element_44);
        const __cope_m3_unwrap_45 = __cope_m3_column_element_44;
        __cope_m3_result_validate_7(__cope_m3_unwrap_45);
        if (__cope_m3_unwrap_45.$tag === "err") {
            __cope_m3_panic_unwrap_1(__cope_m3_unwrap_45.$payload[0]);
        }
        (total = (__cope_m3_ordered_46 + __cope_m3_unwrap_45.$payload[0]));
        (index = (index + 1));
    }
    return total;
}

function cellAccess(iterations) {
    let index = 0;
    let total = 0;
    while ((index < iterations)) {
        const __cope_m3_ordered_52 = total;
        const __cope_m3_table_receiver_47 = __cope_m3_table_value_t1_23;
        __cope_m3_table_require_t1_19(__cope_m3_table_receiver_47);
        const __cope_m3_column_receiver_48 = __cope_m3_table_receiver_47[__cope_m3_table_column___cope_00740031002e00630030_25];
        const __cope_m3_column_index_49 = (index % 10);
        __cope_m3_column_require_14(__cope_m3_column_receiver_48);
        const __cope_m3_column_element_50 = __cope_m3_column_receiver_48[__cope_m3_column_read_13](__cope_m3_column_index_49);
        __cope_m3_result_validate_7(__cope_m3_column_element_50);
        const __cope_m3_unwrap_51 = __cope_m3_column_element_50;
        __cope_m3_result_validate_7(__cope_m3_unwrap_51);
        if (__cope_m3_unwrap_51.$tag === "err") {
            __cope_m3_panic_unwrap_1(__cope_m3_unwrap_51.$payload[0]);
        }
        (total = (__cope_m3_ordered_52 + __cope_m3_unwrap_51.$payload[0]));
        (index = (index + 1));
    }
    return total;
}

function queryAccess(iterations) {
    let index = 0;
    let total = 0;
    while ((index < iterations)) {
        const __cope_m3_ordered_57 = total;
        const __cope_m3_table_receiver_53 = __cope_m3_table_value_t1_23;
        __cope_m3_table_require_t1_19(__cope_m3_table_receiver_53);
        (total = (__cope_m3_ordered_57 + (() => { const __cope_m3_column_input_54 = __cope_m3_table_receiver_53[__cope_m3_table_column___cope_00740031002e00630030_25]; let __cope_m3_column_result_55 = 0; for (let __cope_m3_column_index_56 = 0; __cope_m3_column_index_56 < 10; __cope_m3_column_index_56 += 1) { __cope_m3_column_result_55 += __cope_m3_column_input_54[__cope_m3_column_read_13](__cope_m3_column_index_56).$payload[0]; } return __cope_m3_column_result_55; })()));
        (index = (index + 1));
    }
    return total;
}

function main() {
    return (((rowAccess(100) + columnAccess(100)) + cellAccess(100)) + queryAccess(100));
}
