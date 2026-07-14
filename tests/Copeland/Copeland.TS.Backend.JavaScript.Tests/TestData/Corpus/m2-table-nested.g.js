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

const __cope_m3_record_type_r1_3 = Symbol("r1");
const __cope_m3_record_field___cope_00720031002e00660030_6 = Symbol("r1.f0");

function __cope_m3_record_make_r1_4(field0) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r1_3]: { value: __cope_m3_record_type_r1_3, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660030_6]: { value: field0, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(value);
}

function __cope_m3_record_require_r1_5(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r1_3) || value[__cope_m3_record_type_r1_3] !== __cope_m3_record_type_r1_3 || Object.getOwnPropertySymbols(value).length !== 2 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660030_6)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_type_7 = Object.freeze(Object.create(null));

const __cope_m3_type_9 = Object.freeze(Object.create(null));

function __cope_m3_validate_8(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_7 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
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

function __cope_m3_validate_10(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_9 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Empty":
            if (value.$payload.length !== 0) {
                __cope_m3_panic_0();
            }
            return;
        case "Value":
            if (value.$payload.length !== 1) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !((__cope_m3_record_require_r1_5(value.$payload[0]), true))) {
                __cope_m3_panic_0();
            }
            return;
        default:
            __cope_m3_panic_0();
    }
}

const __cope_m3_result_type_11 = Object.freeze(Object.create(null));

const __cope_m3_result_type_13 = Object.freeze(Object.create(null));

const __cope_m3_result_type_15 = Object.freeze(Object.create(null));

const __cope_m3_result_type_17 = Object.freeze(Object.create(null));

const __cope_m3_result_type_19 = Object.freeze(Object.create(null));

function __cope_m3_result_validate_12(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_11 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_record_require_r1_5(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_8(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_14(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_13 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_validate_10(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_8(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_16(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_15 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_validate_10(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_18(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_17 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_result_validate_16(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_8(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_20(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_19 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_table_row_require_t1_29(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_8(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

const __cope_m3_column_type_21 = Symbol("cope.column");
const __cope_m3_column_read_22 = Symbol("cope.column.read");
const __cope_m3_table_row_table_24 = Symbol("cope.table.row.table");
const __cope_m3_table_row_index_25 = Symbol("cope.table.row.index");

function __cope_m3_column_require_23(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_column_type_21) || value[__cope_m3_column_type_21] !== __cope_m3_column_type_21 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_column_read_22) || typeof value[__cope_m3_column_read_22] !== "function" || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_table_type_t1_26 = Symbol("t1");
const __cope_m3_table_row_type_t1_27 = Symbol("t1.row");
const __cope_m3_table_rows_t1_33 = Symbol("t1.rows");
const __cope_m3_table_column___cope_00740031002e00630030_34 = Symbol("t1.c0");
const __cope_m3_column_type___cope_00740031002e00630030_35 = Symbol("t1.c0.column");
const __cope_m3_table_column___cope_00740031002e00630031_38 = Symbol("t1.c1");
const __cope_m3_column_type___cope_00740031002e00630031_39 = Symbol("t1.c1.column");
const __cope_m3_table_column___cope_00740031002e00630032_42 = Symbol("t1.c2");
const __cope_m3_column_type___cope_00740031002e00630032_43 = Symbol("t1.c2.column");

function __cope_m3_table_require_t1_28(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_type_t1_26) || value[__cope_m3_table_type_t1_26] !== __cope_m3_table_type_t1_26 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_rows_t1_33) || typeof value[__cope_m3_table_rows_t1_33] !== "function" || Object.getOwnPropertySymbols(value).length !== 5) {
        __cope_m3_panic_0();
    }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630030_34)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630031_38)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630032_42)) { __cope_m3_panic_0(); }
}

function __cope_m3_table_row_require_t1_29(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_type_t1_27) || value[__cope_m3_table_row_type_t1_27] !== __cope_m3_table_row_type_t1_27 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_table_24) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_index_25) || !Number.isInteger(value[__cope_m3_table_row_index_25]) || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
    __cope_m3_table_require_t1_28(value[__cope_m3_table_row_table_24]);
}

function __cope_m3_table_row_create_t1_31(tableValue, index) {
    const row = Object.create(null);
    Object.defineProperties(row, {
        [__cope_m3_table_row_type_t1_27]: { value: __cope_m3_table_row_type_t1_27, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_table_24]: { value: tableValue, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_index_25]: { value: index, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(row);
}

function __cope_m3_table_create_t1_30() {
    const __cope_m3_table_storage___cope_00740031002e00630030_36 = Object.freeze([__cope_m3_record_make_r1_4(1), __cope_m3_record_make_r1_4(2)]);
    const __cope_m3_table_column_value___cope_00740031002e00630030_37 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630030_37, {
        [__cope_m3_column_type_21]: { value: __cope_m3_column_type_21, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630030_35]: { value: __cope_m3_column_type___cope_00740031002e00630030_35, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_22]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_11, "err", [__cope_m3_make_2(__cope_m3_type_7, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_11, "err", [__cope_m3_make_2(__cope_m3_type_7, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_11, "ok", [__cope_m3_table_storage___cope_00740031002e00630030_36[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630030_37);
    const __cope_m3_table_storage___cope_00740031002e00630031_40 = Object.freeze([__cope_m3_make_2(__cope_m3_type_9, "Value", [__cope_m3_record_make_r1_4(3)]), __cope_m3_make_2(__cope_m3_type_9, "Empty", [])]);
    const __cope_m3_table_column_value___cope_00740031002e00630031_41 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630031_41, {
        [__cope_m3_column_type_21]: { value: __cope_m3_column_type_21, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630031_39]: { value: __cope_m3_column_type___cope_00740031002e00630031_39, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_22]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_13, "err", [__cope_m3_make_2(__cope_m3_type_7, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_13, "err", [__cope_m3_make_2(__cope_m3_type_7, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_13, "ok", [__cope_m3_table_storage___cope_00740031002e00630031_40[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630031_41);
    const __cope_m3_table_storage___cope_00740031002e00630032_44 = Object.freeze([__cope_m3_make_2(__cope_m3_result_type_15, "ok", [__cope_m3_make_2(__cope_m3_type_9, "Value", [__cope_m3_record_make_r1_4(4)])]), __cope_m3_make_2(__cope_m3_result_type_15, "err", ["bad"])]);
    const __cope_m3_table_column_value___cope_00740031002e00630032_45 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630032_45, {
        [__cope_m3_column_type_21]: { value: __cope_m3_column_type_21, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630032_43]: { value: __cope_m3_column_type___cope_00740031002e00630032_43, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_22]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_17, "err", [__cope_m3_make_2(__cope_m3_type_7, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_17, "err", [__cope_m3_make_2(__cope_m3_type_7, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_17, "ok", [__cope_m3_table_storage___cope_00740031002e00630032_44[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630032_45);
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_table_type_t1_26]: { value: __cope_m3_table_type_t1_26, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_rows_t1_33]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_19, "err", [__cope_m3_make_2(__cope_m3_type_7, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_19, "err", [__cope_m3_make_2(__cope_m3_type_7, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_19, "ok", [__cope_m3_table_row_create_t1_31(value, index)]);
        }, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630030_34]: { value: __cope_m3_table_column_value___cope_00740031002e00630030_37, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630031_38]: { value: __cope_m3_table_column_value___cope_00740031002e00630031_41, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630032_42]: { value: __cope_m3_table_column_value___cope_00740031002e00630032_45, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(value);
}

const __cope_m3_table_value_t1_32 = __cope_m3_table_create_t1_30();

function main() {
    const __cope_m3_table_receiver_46 = __cope_m3_table_value_t1_32;
    const __cope_m3_table_index_47 = 1;
    __cope_m3_table_require_t1_28(__cope_m3_table_receiver_46);
    const __cope_m3_table_row_48 = __cope_m3_table_receiver_46[__cope_m3_table_rows_t1_33](__cope_m3_table_index_47);
    __cope_m3_result_validate_20(__cope_m3_table_row_48);
    const __cope_m3_unwrap_49 = __cope_m3_table_row_48;
    __cope_m3_result_validate_20(__cope_m3_unwrap_49);
    if (__cope_m3_unwrap_49.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_49.$payload[0]);
    }
    const row = __cope_m3_unwrap_49.$payload[0];
    const __cope_m3_table_row_50 = row;
    __cope_m3_table_row_require_t1_29(__cope_m3_table_row_50);
    const __cope_m3_row_table_51 = __cope_m3_table_row_50[__cope_m3_table_row_table_24];
    const __cope_m3_row_field_52 = __cope_m3_row_table_51[__cope_m3_table_column___cope_00740031002e00630030_34][__cope_m3_column_read_22](__cope_m3_table_row_50[__cope_m3_table_row_index_25]);
    __cope_m3_result_validate_12(__cope_m3_row_field_52);
    if (__cope_m3_row_field_52.$tag !== "ok") { __cope_m3_panic_0(); }
    const __cope_m3_record_receiver_53 = __cope_m3_row_field_52.$payload[0];
    __cope_m3_record_require_r1_5(__cope_m3_record_receiver_53);
    const __cope_m3_ordered_64 = __cope_m3_record_receiver_53[__cope_m3_record_field___cope_00720031002e00660030_6];
    const __cope_m3_table_receiver_54 = __cope_m3_table_value_t1_32;
    __cope_m3_table_require_t1_28(__cope_m3_table_receiver_54);
    const __cope_m3_column_receiver_55 = __cope_m3_table_receiver_54[__cope_m3_table_column___cope_00740031002e00630032_42];
    const __cope_m3_column_index_56 = 0;
    __cope_m3_column_require_23(__cope_m3_column_receiver_55);
    const __cope_m3_column_element_57 = __cope_m3_column_receiver_55[__cope_m3_column_read_22](__cope_m3_column_index_56);
    __cope_m3_result_validate_18(__cope_m3_column_element_57);
    const __cope_m3_unwrap_58 = __cope_m3_column_element_57;
    __cope_m3_result_validate_18(__cope_m3_unwrap_58);
    if (__cope_m3_unwrap_58.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_58.$payload[0]);
    }
    const __cope_m3_result_match_62 = __cope_m3_unwrap_58.$payload[0];
    __cope_m3_result_validate_16(__cope_m3_result_match_62);
    let __cope_m3_result_value_63;
    switch (__cope_m3_result_match_62.$tag) {
        case "ok": {
            const value = __cope_m3_result_match_62.$payload[0];
            const __cope_m3_match_59 = value;
            __cope_m3_validate_10(__cope_m3_match_59);
            let __cope_m3_match_value_60;
            switch (__cope_m3_match_59.$tag) {
                case "Value":
                {
                    const point = __cope_m3_match_59.$payload[0];
                    const __cope_m3_record_receiver_61 = point;
                    __cope_m3_record_require_r1_5(__cope_m3_record_receiver_61);
                    __cope_m3_match_value_60 = __cope_m3_record_receiver_61[__cope_m3_record_field___cope_00720031002e00660030_6];
                    break;
                }
                case "Empty":
                {
                    __cope_m3_match_value_60 = 0;
                    break;
                }
                default:
                    __cope_m3_panic_0();
            }
            __cope_m3_result_value_63 = __cope_m3_match_value_60;
            break;
        }
        case "err": {
            const error = __cope_m3_result_match_62.$payload[0];
            __cope_m3_result_value_63 = 0;
            break;
        }
        default:
            __cope_m3_panic_0();
    }
    return (__cope_m3_ordered_64 + __cope_m3_result_value_63);
}
