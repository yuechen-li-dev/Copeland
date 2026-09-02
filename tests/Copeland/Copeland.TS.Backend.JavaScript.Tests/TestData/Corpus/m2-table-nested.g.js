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
    if (type === __cope_m3_type_8) __cope_m3_instances_9.add(value);
    if (type === __cope_m3_type_11) __cope_m3_instances_12.add(value);
    return value;
}

const __cope_m3_record_type_r1_3 = Symbol("r1");
const __cope_m3_record_instances_r1_4 = new WeakSet();
const __cope_m3_record_field___cope_00720031002e00660030_7 = Symbol("r1.f0");

function __cope_m3_record_make_r1_5(field0) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r1_3]: { value: __cope_m3_record_type_r1_3, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660030_7]: { value: field0, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r1_4.add(value);
    return value;
}

function __cope_m3_record_require_r1_6(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r1_4.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r1_3) || value[__cope_m3_record_type_r1_3] !== __cope_m3_record_type_r1_3 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660030_7)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_type_8 = Object.freeze(Object.create(null));
const __cope_m3_instances_9 = new WeakSet();

const __cope_m3_type_11 = Object.freeze(Object.create(null));
const __cope_m3_instances_12 = new WeakSet();

function __cope_m3_validate_10(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_9.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_8 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
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

function __cope_m3_validate_13(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_12.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_11 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
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
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !((__cope_m3_record_require_r1_6(value.$payload[0]), true))) {
                __cope_m3_panic_0();
            }
            return;
        default:
            __cope_m3_panic_0();
    }
}

const __cope_m3_result_type_14 = Object.freeze(Object.create(null));

const __cope_m3_result_type_16 = Object.freeze(Object.create(null));

const __cope_m3_result_type_18 = Object.freeze(Object.create(null));

const __cope_m3_result_type_20 = Object.freeze(Object.create(null));

const __cope_m3_result_type_22 = Object.freeze(Object.create(null));

function __cope_m3_result_validate_15(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_14 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_record_require_r1_6(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_10(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_19(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_18 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_validate_13(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_21(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_20 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_result_validate_19(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_10(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_23(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_22 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_table_row_require_t1_32(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_10(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

const __cope_m3_column_type_24 = Symbol("cope.column");
const __cope_m3_column_read_25 = Symbol("cope.column.read");
const __cope_m3_table_row_table_27 = Symbol("cope.table.row.table");
const __cope_m3_table_row_index_28 = Symbol("cope.table.row.index");

function __cope_m3_column_require_26(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_column_type_24) || value[__cope_m3_column_type_24] !== __cope_m3_column_type_24 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_column_read_25) || typeof value[__cope_m3_column_read_25] !== "function" || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_table_type_t1_29 = Symbol("t1");
const __cope_m3_table_row_type_t1_30 = Symbol("t1.row");
const __cope_m3_table_rows_t1_36 = Symbol("t1.rows");
const __cope_m3_table_column___cope_00740031002e00630030_37 = Symbol("t1.c0");
const __cope_m3_column_type___cope_00740031002e00630030_38 = Symbol("t1.c0.column");
const __cope_m3_table_column___cope_00740031002e00630031_41 = Symbol("t1.c1");
const __cope_m3_column_type___cope_00740031002e00630031_42 = Symbol("t1.c1.column");
const __cope_m3_table_column___cope_00740031002e00630032_45 = Symbol("t1.c2");
const __cope_m3_column_type___cope_00740031002e00630032_46 = Symbol("t1.c2.column");

function __cope_m3_table_require_t1_31(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_type_t1_29) || value[__cope_m3_table_type_t1_29] !== __cope_m3_table_type_t1_29 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_rows_t1_36) || typeof value[__cope_m3_table_rows_t1_36] !== "function" || Object.getOwnPropertySymbols(value).length !== 5) {
        __cope_m3_panic_0();
    }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630030_37)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630031_41)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630032_45)) { __cope_m3_panic_0(); }
}

function __cope_m3_table_row_require_t1_32(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_type_t1_30) || value[__cope_m3_table_row_type_t1_30] !== __cope_m3_table_row_type_t1_30 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_table_27) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_index_28) || !Number.isInteger(value[__cope_m3_table_row_index_28]) || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
    __cope_m3_table_require_t1_31(value[__cope_m3_table_row_table_27]);
}

function __cope_m3_table_row_create_t1_34(tableValue, index) {
    const row = Object.create(null);
    Object.defineProperties(row, {
        [__cope_m3_table_row_type_t1_30]: { value: __cope_m3_table_row_type_t1_30, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_table_27]: { value: tableValue, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_index_28]: { value: index, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(row);
}

function __cope_m3_table_create_t1_33() {
    const __cope_m3_table_storage___cope_00740031002e00630030_39 = Object.freeze([__cope_m3_record_make_r1_5(1), __cope_m3_record_make_r1_5(2)]);
    const __cope_m3_table_column_value___cope_00740031002e00630030_40 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630030_40, {
        [__cope_m3_column_type_24]: { value: __cope_m3_column_type_24, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630030_38]: { value: __cope_m3_column_type___cope_00740031002e00630030_38, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_25]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_14, "err", [__cope_m3_make_2(__cope_m3_type_8, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_14, "err", [__cope_m3_make_2(__cope_m3_type_8, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_14, "ok", [__cope_m3_table_storage___cope_00740031002e00630030_39[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630030_40);
    const __cope_m3_table_storage___cope_00740031002e00630031_43 = Object.freeze([__cope_m3_make_2(__cope_m3_type_11, "Value", [__cope_m3_record_make_r1_5(3)]), __cope_m3_make_2(__cope_m3_type_11, "Empty", [])]);
    const __cope_m3_table_column_value___cope_00740031002e00630031_44 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630031_44, {
        [__cope_m3_column_type_24]: { value: __cope_m3_column_type_24, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630031_42]: { value: __cope_m3_column_type___cope_00740031002e00630031_42, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_25]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_16, "err", [__cope_m3_make_2(__cope_m3_type_8, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_16, "err", [__cope_m3_make_2(__cope_m3_type_8, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_16, "ok", [__cope_m3_table_storage___cope_00740031002e00630031_43[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630031_44);
    const __cope_m3_table_storage___cope_00740031002e00630032_47 = Object.freeze([__cope_m3_make_2(__cope_m3_result_type_18, "ok", [__cope_m3_make_2(__cope_m3_type_11, "Value", [__cope_m3_record_make_r1_5(4)])]), __cope_m3_make_2(__cope_m3_result_type_18, "err", ["bad"])]);
    const __cope_m3_table_column_value___cope_00740031002e00630032_48 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630032_48, {
        [__cope_m3_column_type_24]: { value: __cope_m3_column_type_24, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630032_46]: { value: __cope_m3_column_type___cope_00740031002e00630032_46, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_25]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_20, "err", [__cope_m3_make_2(__cope_m3_type_8, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_20, "err", [__cope_m3_make_2(__cope_m3_type_8, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_20, "ok", [__cope_m3_table_storage___cope_00740031002e00630032_47[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630032_48);
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_table_type_t1_29]: { value: __cope_m3_table_type_t1_29, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_rows_t1_36]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_22, "err", [__cope_m3_make_2(__cope_m3_type_8, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_22, "err", [__cope_m3_make_2(__cope_m3_type_8, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_22, "ok", [__cope_m3_table_row_create_t1_34(value, index)]);
        }, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630030_37]: { value: __cope_m3_table_column_value___cope_00740031002e00630030_40, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630031_41]: { value: __cope_m3_table_column_value___cope_00740031002e00630031_44, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630032_45]: { value: __cope_m3_table_column_value___cope_00740031002e00630032_48, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(value);
}

const __cope_m3_table_value_t1_35 = __cope_m3_table_create_t1_33();

function main() {
    const __cope_m3_table_receiver_49 = __cope_m3_table_value_t1_35;
    const __cope_m3_table_index_50 = 1;
    __cope_m3_table_require_t1_31(__cope_m3_table_receiver_49);
    const __cope_m3_table_row_51 = __cope_m3_table_receiver_49[__cope_m3_table_rows_t1_36](__cope_m3_table_index_50);
    __cope_m3_result_validate_23(__cope_m3_table_row_51);
    const __cope_m3_unwrap_52 = __cope_m3_table_row_51;
    __cope_m3_result_validate_23(__cope_m3_unwrap_52);
    if (__cope_m3_unwrap_52.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_52.$payload[0]);
    }
    const row = __cope_m3_unwrap_52.$payload[0];
    const __cope_m3_table_row_53 = row;
    __cope_m3_table_row_require_t1_32(__cope_m3_table_row_53);
    const __cope_m3_row_table_54 = __cope_m3_table_row_53[__cope_m3_table_row_table_27];
    const __cope_m3_row_field_55 = __cope_m3_row_table_54[__cope_m3_table_column___cope_00740031002e00630030_37][__cope_m3_column_read_25](__cope_m3_table_row_53[__cope_m3_table_row_index_28]);
    __cope_m3_result_validate_15(__cope_m3_row_field_55);
    if (__cope_m3_row_field_55.$tag !== "ok") { __cope_m3_panic_0(); }
    const __cope_m3_record_receiver_56 = __cope_m3_row_field_55.$payload[0];
    __cope_m3_record_require_r1_6(__cope_m3_record_receiver_56);
    const __cope_m3_ordered_67 = __cope_m3_record_receiver_56[__cope_m3_record_field___cope_00720031002e00660030_7];
    const __cope_m3_table_receiver_57 = __cope_m3_table_value_t1_35;
    __cope_m3_table_require_t1_31(__cope_m3_table_receiver_57);
    const __cope_m3_column_receiver_58 = __cope_m3_table_receiver_57[__cope_m3_table_column___cope_00740031002e00630032_45];
    const __cope_m3_column_index_59 = 0;
    __cope_m3_column_require_26(__cope_m3_column_receiver_58);
    const __cope_m3_column_element_60 = __cope_m3_column_receiver_58[__cope_m3_column_read_25](__cope_m3_column_index_59);
    __cope_m3_result_validate_21(__cope_m3_column_element_60);
    const __cope_m3_unwrap_61 = __cope_m3_column_element_60;
    __cope_m3_result_validate_21(__cope_m3_unwrap_61);
    if (__cope_m3_unwrap_61.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_61.$payload[0]);
    }
    const __cope_m3_result_match_65 = __cope_m3_unwrap_61.$payload[0];
    __cope_m3_result_validate_19(__cope_m3_result_match_65);
    let __cope_m3_result_value_66;
    switch (__cope_m3_result_match_65.$tag) {
        case "ok": {
            const value = __cope_m3_result_match_65.$payload[0];
            const __cope_m3_match_62 = value;
            __cope_m3_validate_13(__cope_m3_match_62);
            let __cope_m3_match_value_63;
            switch (__cope_m3_match_62.$tag) {
                case "Value":
                {
                    const point = __cope_m3_match_62.$payload[0];
                    const __cope_m3_record_receiver_64 = point;
                    __cope_m3_record_require_r1_6(__cope_m3_record_receiver_64);
                    __cope_m3_match_value_63 = __cope_m3_record_receiver_64[__cope_m3_record_field___cope_00720031002e00660030_7];
                    break;
                }
                case "Empty":
                {
                    __cope_m3_match_value_63 = 0;
                    break;
                }
                default:
                    __cope_m3_panic_0();
            }
            __cope_m3_result_value_66 = __cope_m3_match_value_63;
            break;
        }
        case "err": {
            const error = __cope_m3_result_match_65.$payload[0];
            __cope_m3_result_value_66 = 0;
            break;
        }
        default:
            __cope_m3_panic_0();
    }
    return (__cope_m3_ordered_67 + __cope_m3_result_value_66);
}
