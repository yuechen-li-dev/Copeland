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
    if (type === __cope_m3_type_9) __cope_m3_instances_10.add(value);
    if (type === __cope_m3_type_12) __cope_m3_instances_13.add(value);
    return value;
}

const __cope_m3_record_type_r1_3 = Symbol("r1");
const __cope_m3_record_instances_r1_4 = new WeakSet();
const __cope_m3_record_field___cope_00720031002e00660030_7 = Symbol("r1.f0");
const __cope_m3_record_field___cope_00720031002e00660031_8 = Symbol("r1.f1");

function __cope_m3_record_make_r1_5(field0, field1) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r1_3]: { value: __cope_m3_record_type_r1_3, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660030_7]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660031_8]: { value: field1, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r1_4.add(value);
    return value;
}

function __cope_m3_record_require_r1_6(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r1_4.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r1_3) || value[__cope_m3_record_type_r1_3] !== __cope_m3_record_type_r1_3 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660030_7) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660031_8)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_type_9 = Object.freeze(Object.create(null));
const __cope_m3_instances_10 = new WeakSet();

const __cope_m3_type_12 = Object.freeze(Object.create(null));
const __cope_m3_instances_13 = new WeakSet();

function __cope_m3_validate_11(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_10.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_9 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
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

function __cope_m3_validate_14(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_13.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_12 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Missing":
            if (value.$payload.length !== 0) {
                __cope_m3_panic_0();
            }
            return;
        case "Named":
            if (value.$payload.length !== 1) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "string")) {
                __cope_m3_panic_0();
            }
            return;
        default:
            __cope_m3_panic_0();
    }
}

const __cope_m3_result_type_15 = Object.freeze(Object.create(null));

const __cope_m3_result_type_17 = Object.freeze(Object.create(null));

const __cope_m3_result_type_19 = Object.freeze(Object.create(null));

const __cope_m3_result_type_21 = Object.freeze(Object.create(null));

const __cope_m3_result_type_23 = Object.freeze(Object.create(null));

const __cope_m3_result_type_25 = Object.freeze(Object.create(null));

const __cope_m3_result_type_27 = Object.freeze(Object.create(null));

const __cope_m3_result_type_29 = Object.freeze(Object.create(null));

function __cope_m3_result_validate_16(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_15 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!(typeof value.$payload[0] === "boolean")) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_11(value.$payload[0]), true))) { __cope_m3_panic_0(); }
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
            if (!(typeof value.$payload[0] === "number")) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_11(value.$payload[0]), true))) { __cope_m3_panic_0(); }
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
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_11(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_22(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_21 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_record_require_r1_6(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_11(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_24(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_23 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_validate_14(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_11(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_26(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_25 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!(Array.isArray(value.$payload[0]))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_11(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_28(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_27 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_table_row_require_t1_39(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_11(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_30(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_29 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_table_row_require_t2_71(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_11(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

const __cope_m3_column_type_31 = Symbol("cope.column");
const __cope_m3_column_read_32 = Symbol("cope.column.read");
const __cope_m3_table_row_table_34 = Symbol("cope.table.row.table");
const __cope_m3_table_row_index_35 = Symbol("cope.table.row.index");

function __cope_m3_column_require_33(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_column_type_31) || value[__cope_m3_column_type_31] !== __cope_m3_column_type_31 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_column_read_32) || typeof value[__cope_m3_column_read_32] !== "function" || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_table_type_t1_36 = Symbol("t1");
const __cope_m3_table_row_type_t1_37 = Symbol("t1.row");
const __cope_m3_table_rows_t1_43 = Symbol("t1.rows");
const __cope_m3_table_column___cope_00740031002e00630030_44 = Symbol("t1.c0");
const __cope_m3_column_type___cope_00740031002e00630030_45 = Symbol("t1.c0.column");
const __cope_m3_table_column___cope_00740031002e00630031_48 = Symbol("t1.c1");
const __cope_m3_column_type___cope_00740031002e00630031_49 = Symbol("t1.c1.column");
const __cope_m3_table_column___cope_00740031002e00630032_52 = Symbol("t1.c2");
const __cope_m3_column_type___cope_00740031002e00630032_53 = Symbol("t1.c2.column");
const __cope_m3_table_column___cope_00740031002e00630033_56 = Symbol("t1.c3");
const __cope_m3_column_type___cope_00740031002e00630033_57 = Symbol("t1.c3.column");
const __cope_m3_table_column___cope_00740031002e00630034_60 = Symbol("t1.c4");
const __cope_m3_column_type___cope_00740031002e00630034_61 = Symbol("t1.c4.column");
const __cope_m3_table_column___cope_00740031002e00630035_64 = Symbol("t1.c5");
const __cope_m3_column_type___cope_00740031002e00630035_65 = Symbol("t1.c5.column");

function __cope_m3_table_require_t1_38(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_type_t1_36) || value[__cope_m3_table_type_t1_36] !== __cope_m3_table_type_t1_36 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_rows_t1_43) || typeof value[__cope_m3_table_rows_t1_43] !== "function" || Object.getOwnPropertySymbols(value).length !== 8) {
        __cope_m3_panic_0();
    }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630030_44)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630031_48)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630032_52)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630033_56)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630034_60)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630035_64)) { __cope_m3_panic_0(); }
}

function __cope_m3_table_row_require_t1_39(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_type_t1_37) || value[__cope_m3_table_row_type_t1_37] !== __cope_m3_table_row_type_t1_37 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_table_34) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_index_35) || !Number.isInteger(value[__cope_m3_table_row_index_35]) || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
    __cope_m3_table_require_t1_38(value[__cope_m3_table_row_table_34]);
}

function __cope_m3_table_row_create_t1_41(tableValue, index) {
    const row = Object.create(null);
    Object.defineProperties(row, {
        [__cope_m3_table_row_type_t1_37]: { value: __cope_m3_table_row_type_t1_37, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_table_34]: { value: tableValue, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_index_35]: { value: index, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(row);
}

function __cope_m3_table_create_t1_40() {
    const __cope_m3_table_storage___cope_00740031002e00630030_46 = Object.freeze([true, false]);
    const __cope_m3_table_column_value___cope_00740031002e00630030_47 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630030_47, {
        [__cope_m3_column_type_31]: { value: __cope_m3_column_type_31, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630030_45]: { value: __cope_m3_column_type___cope_00740031002e00630030_45, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_32]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_15, "err", [__cope_m3_make_2(__cope_m3_type_9, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_15, "err", [__cope_m3_make_2(__cope_m3_type_9, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_15, "ok", [__cope_m3_table_storage___cope_00740031002e00630030_46[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630030_47);
    const __cope_m3_table_storage___cope_00740031002e00630031_50 = Object.freeze([-0, NaN]);
    const __cope_m3_table_column_value___cope_00740031002e00630031_51 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630031_51, {
        [__cope_m3_column_type_31]: { value: __cope_m3_column_type_31, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630031_49]: { value: __cope_m3_column_type___cope_00740031002e00630031_49, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_32]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_17, "err", [__cope_m3_make_2(__cope_m3_type_9, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_17, "err", [__cope_m3_make_2(__cope_m3_type_9, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_17, "ok", [__cope_m3_table_storage___cope_00740031002e00630031_50[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630031_51);
    const __cope_m3_table_storage___cope_00740031002e00630032_54 = Object.freeze(["quote \" slash \\ newline\n", "雪 \ud83d\ude00"]);
    const __cope_m3_table_column_value___cope_00740031002e00630032_55 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630032_55, {
        [__cope_m3_column_type_31]: { value: __cope_m3_column_type_31, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630032_53]: { value: __cope_m3_column_type___cope_00740031002e00630032_53, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_32]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_19, "err", [__cope_m3_make_2(__cope_m3_type_9, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_19, "err", [__cope_m3_make_2(__cope_m3_type_9, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_19, "ok", [__cope_m3_table_storage___cope_00740031002e00630032_54[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630032_55);
    const __cope_m3_table_storage___cope_00740031002e00630033_58 = Object.freeze([__cope_m3_record_make_r1_5(1, "first"), __cope_m3_record_make_r1_5(2, "second")]);
    const __cope_m3_table_column_value___cope_00740031002e00630033_59 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630033_59, {
        [__cope_m3_column_type_31]: { value: __cope_m3_column_type_31, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630033_57]: { value: __cope_m3_column_type___cope_00740031002e00630033_57, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_32]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_21, "err", [__cope_m3_make_2(__cope_m3_type_9, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_21, "err", [__cope_m3_make_2(__cope_m3_type_9, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_21, "ok", [__cope_m3_table_storage___cope_00740031002e00630033_58[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630033_59);
    const __cope_m3_table_storage___cope_00740031002e00630034_62 = Object.freeze([__cope_m3_make_2(__cope_m3_type_12, "Missing", []), __cope_m3_make_2(__cope_m3_type_12, "Named", ["ready"])]);
    const __cope_m3_table_column_value___cope_00740031002e00630034_63 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630034_63, {
        [__cope_m3_column_type_31]: { value: __cope_m3_column_type_31, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630034_61]: { value: __cope_m3_column_type___cope_00740031002e00630034_61, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_32]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_23, "err", [__cope_m3_make_2(__cope_m3_type_9, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_23, "err", [__cope_m3_make_2(__cope_m3_type_9, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_23, "ok", [__cope_m3_table_storage___cope_00740031002e00630034_62[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630034_63);
    const __cope_m3_table_storage___cope_00740031002e00630035_66 = Object.freeze([Object.freeze([Object.freeze([]), Object.freeze([1, 2])]), Object.freeze([Object.freeze([3]), Object.freeze([])])]);
    const __cope_m3_table_column_value___cope_00740031002e00630035_67 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630035_67, {
        [__cope_m3_column_type_31]: { value: __cope_m3_column_type_31, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630035_65]: { value: __cope_m3_column_type___cope_00740031002e00630035_65, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_32]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_25, "err", [__cope_m3_make_2(__cope_m3_type_9, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_25, "err", [__cope_m3_make_2(__cope_m3_type_9, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_25, "ok", [__cope_m3_table_storage___cope_00740031002e00630035_66[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630035_67);
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_table_type_t1_36]: { value: __cope_m3_table_type_t1_36, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_rows_t1_43]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_27, "err", [__cope_m3_make_2(__cope_m3_type_9, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 2) {
                return __cope_m3_make_2(__cope_m3_result_type_27, "err", [__cope_m3_make_2(__cope_m3_type_9, "OutOfBounds", [index, 2])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_27, "ok", [__cope_m3_table_row_create_t1_41(value, index)]);
        }, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630030_44]: { value: __cope_m3_table_column_value___cope_00740031002e00630030_47, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630031_48]: { value: __cope_m3_table_column_value___cope_00740031002e00630031_51, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630032_52]: { value: __cope_m3_table_column_value___cope_00740031002e00630032_55, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630033_56]: { value: __cope_m3_table_column_value___cope_00740031002e00630033_59, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630034_60]: { value: __cope_m3_table_column_value___cope_00740031002e00630034_63, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630035_64]: { value: __cope_m3_table_column_value___cope_00740031002e00630035_67, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(value);
}

const __cope_m3_table_value_t1_42 = __cope_m3_table_create_t1_40();

const __cope_m3_table_type_t2_68 = Symbol("t2");
const __cope_m3_table_row_type_t2_69 = Symbol("t2.row");
const __cope_m3_table_rows_t2_75 = Symbol("t2.rows");
const __cope_m3_table_column___cope_00740032002e00630030_76 = Symbol("t2.c0");
const __cope_m3_column_type___cope_00740032002e00630030_77 = Symbol("t2.c0.column");

function __cope_m3_table_require_t2_70(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_type_t2_68) || value[__cope_m3_table_type_t2_68] !== __cope_m3_table_type_t2_68 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_rows_t2_75) || typeof value[__cope_m3_table_rows_t2_75] !== "function" || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740032002e00630030_76)) { __cope_m3_panic_0(); }
}

function __cope_m3_table_row_require_t2_71(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_type_t2_69) || value[__cope_m3_table_row_type_t2_69] !== __cope_m3_table_row_type_t2_69 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_table_34) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_index_35) || !Number.isInteger(value[__cope_m3_table_row_index_35]) || Object.getOwnPropertySymbols(value).length !== 3) {
        __cope_m3_panic_0();
    }
    __cope_m3_table_require_t2_70(value[__cope_m3_table_row_table_34]);
}

function __cope_m3_table_row_create_t2_73(tableValue, index) {
    const row = Object.create(null);
    Object.defineProperties(row, {
        [__cope_m3_table_row_type_t2_69]: { value: __cope_m3_table_row_type_t2_69, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_table_34]: { value: tableValue, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_index_35]: { value: index, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(row);
}

function __cope_m3_table_create_t2_72() {
    const __cope_m3_table_storage___cope_00740032002e00630030_78 = Object.freeze([]);
    const __cope_m3_table_column_value___cope_00740032002e00630030_79 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740032002e00630030_79, {
        [__cope_m3_column_type_31]: { value: __cope_m3_column_type_31, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740032002e00630030_77]: { value: __cope_m3_column_type___cope_00740032002e00630030_77, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_32]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_17, "err", [__cope_m3_make_2(__cope_m3_type_9, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 0) {
                return __cope_m3_make_2(__cope_m3_result_type_17, "err", [__cope_m3_make_2(__cope_m3_type_9, "OutOfBounds", [index, 0])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_17, "ok", [__cope_m3_table_storage___cope_00740032002e00630030_78[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740032002e00630030_79);
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_table_type_t2_68]: { value: __cope_m3_table_type_t2_68, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_rows_t2_75]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_29, "err", [__cope_m3_make_2(__cope_m3_type_9, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 0) {
                return __cope_m3_make_2(__cope_m3_result_type_29, "err", [__cope_m3_make_2(__cope_m3_type_9, "OutOfBounds", [index, 0])]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_29, "ok", [__cope_m3_table_row_create_t2_73(value, index)]);
        }, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740032002e00630030_76]: { value: __cope_m3_table_column_value___cope_00740032002e00630030_79, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(value);
}

const __cope_m3_table_value_t2_74 = __cope_m3_table_create_t2_72();

function observation() {
    const __cope_m3_table_receiver_80 = __cope_m3_table_value_t1_42;
    const __cope_m3_table_index_81 = 1;
    __cope_m3_table_require_t1_38(__cope_m3_table_receiver_80);
    const __cope_m3_table_row_82 = __cope_m3_table_receiver_80[__cope_m3_table_rows_t1_43](__cope_m3_table_index_81);
    __cope_m3_result_validate_28(__cope_m3_table_row_82);
    const __cope_m3_unwrap_83 = __cope_m3_table_row_82;
    __cope_m3_result_validate_28(__cope_m3_unwrap_83);
    if (__cope_m3_unwrap_83.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_83.$payload[0]);
    }
    const row = __cope_m3_unwrap_83.$payload[0];
    const __cope_m3_table_row_84 = row;
    __cope_m3_table_row_require_t1_39(__cope_m3_table_row_84);
    const __cope_m3_row_table_85 = __cope_m3_table_row_84[__cope_m3_table_row_table_34];
    const __cope_m3_row_field_86 = __cope_m3_row_table_85[__cope_m3_table_column___cope_00740031002e00630034_60][__cope_m3_column_read_32](__cope_m3_table_row_84[__cope_m3_table_row_index_35]);
    __cope_m3_result_validate_24(__cope_m3_row_field_86);
    if (__cope_m3_row_field_86.$tag !== "ok") { __cope_m3_panic_0(); }
    const __cope_m3_match_87 = __cope_m3_row_field_86.$payload[0];
    __cope_m3_validate_14(__cope_m3_match_87);
    let __cope_m3_match_value_88;
    switch (__cope_m3_match_87.$tag) {
        case "Missing":
        {
            __cope_m3_match_value_88 = "missing";
            break;
        }
        case "Named":
        {
            const label = __cope_m3_match_87.$payload[0];
            __cope_m3_match_value_88 = label;
            break;
        }
        default:
            __cope_m3_panic_0();
    }
    return __cope_m3_match_value_88;
}

function negativeZero() {
    const __cope_m3_table_receiver_89 = __cope_m3_table_value_t1_42;
    __cope_m3_table_require_t1_38(__cope_m3_table_receiver_89);
    const __cope_m3_column_receiver_90 = __cope_m3_table_receiver_89[__cope_m3_table_column___cope_00740031002e00630031_48];
    const __cope_m3_column_index_91 = 0;
    __cope_m3_column_require_33(__cope_m3_column_receiver_90);
    const __cope_m3_column_element_92 = __cope_m3_column_receiver_90[__cope_m3_column_read_32](__cope_m3_column_index_91);
    __cope_m3_result_validate_18(__cope_m3_column_element_92);
    const __cope_m3_unwrap_93 = __cope_m3_column_element_92;
    __cope_m3_result_validate_18(__cope_m3_unwrap_93);
    if (__cope_m3_unwrap_93.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_93.$payload[0]);
    }
    return __cope_m3_unwrap_93.$payload[0];
}

function nested() {
    const __cope_m3_table_receiver_94 = __cope_m3_table_value_t1_42;
    __cope_m3_table_require_t1_38(__cope_m3_table_receiver_94);
    const __cope_m3_column_receiver_95 = __cope_m3_table_receiver_94[__cope_m3_table_column___cope_00740031002e00630035_64];
    const __cope_m3_column_index_96 = 1;
    __cope_m3_column_require_33(__cope_m3_column_receiver_95);
    const __cope_m3_column_element_97 = __cope_m3_column_receiver_95[__cope_m3_column_read_32](__cope_m3_column_index_96);
    __cope_m3_result_validate_26(__cope_m3_column_element_97);
    const __cope_m3_unwrap_98 = __cope_m3_column_element_97;
    __cope_m3_result_validate_26(__cope_m3_unwrap_98);
    if (__cope_m3_unwrap_98.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_98.$payload[0]);
    }
    return __cope_m3_unwrap_98.$payload[0];
}

function emptyBounds() {
    const __cope_m3_table_receiver_99 = __cope_m3_table_value_t2_74;
    __cope_m3_table_require_t2_70(__cope_m3_table_receiver_99);
    const __cope_m3_column_receiver_100 = __cope_m3_table_receiver_99[__cope_m3_table_column___cope_00740032002e00630030_76];
    const __cope_m3_column_index_101 = 0;
    __cope_m3_column_require_33(__cope_m3_column_receiver_100);
    const __cope_m3_column_element_102 = __cope_m3_column_receiver_100[__cope_m3_column_read_32](__cope_m3_column_index_101);
    __cope_m3_result_validate_18(__cope_m3_column_element_102);
    const __cope_m3_result_match_104 = __cope_m3_column_element_102;
    __cope_m3_result_validate_18(__cope_m3_result_match_104);
    let __cope_m3_result_value_105;
    switch (__cope_m3_result_match_104.$tag) {
        case "ok": {
            const value = __cope_m3_result_match_104.$payload[0];
            __cope_m3_result_value_105 = value;
            break;
        }
        case "err": {
            const error = __cope_m3_result_match_104.$payload[0];
            __cope_m3_result_value_105 = (() => { const __cope_m3_match_103 = error; __cope_m3_validate_11(__cope_m3_match_103); switch (__cope_m3_match_103.$tag) { case "InvalidIndex": { const index = __cope_m3_match_103.$payload[0]; return 1000; } case "OutOfBounds": { const index = __cope_m3_match_103.$payload[0]; const rowCount = __cope_m3_match_103.$payload[1]; return 2000; } default: return __cope_m3_panic_0(); } })();
            break;
        }
        default:
            __cope_m3_panic_0();
    }
    return __cope_m3_result_value_105;
}
