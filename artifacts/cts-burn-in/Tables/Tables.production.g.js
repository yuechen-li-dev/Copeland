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

const __cope_m3_record_type_r1_3 = Symbol("r1");

function __cope_m3_record_make_r1_5(field0, field1) {
    return { [__cope_m3_record_type_r1_3]: __cope_m3_record_type_r1_3, $f0: field0, $f1: field1 };
}

function __cope_m3_record_require_r1_6(value) {
    if (typeof value !== "object" || value === null || value[__cope_m3_record_type_r1_3] !== __cope_m3_record_type_r1_3 || Object.keys(value).length !== 2 || Object.getOwnPropertySymbols(value).length !== 1 || !Object.prototype.hasOwnProperty.call(value, "$f0") || !Object.prototype.hasOwnProperty.call(value, "$f1")) {
        __cope_m3_panic_0();
    }
    if (!(typeof value.$f0 === "number")) { __cope_m3_panic_0(); }
    if (!(typeof value.$f1 === "number")) { __cope_m3_panic_0(); }
}

const __cope_m3_type_9 = Symbol("TableBoundsError");

const __cope_m3_type_12 = Symbol("ReadingState");
const __cope_m3_type_12_case_0 = Object.freeze({ $type: __cope_m3_type_12, $tag: "Missing" });

function __cope_m3_validate_11(value) {
    if (typeof value !== "object" || value === null || value.$type !== __cope_m3_type_9 || typeof value.$tag !== "string") {
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

function __cope_m3_validate_14(value) {
    if (typeof value !== "object" || value === null || value.$type !== __cope_m3_type_12 || typeof value.$tag !== "string") {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Missing":
            if (Object.keys(value).length !== 2 || Object.getOwnPropertySymbols(value).length !== 0) {
                __cope_m3_panic_0();
            }
            return;
        case "Present":
            if (Object.keys(value).length !== 3 || Object.getOwnPropertySymbols(value).length !== 0) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value, "$p0") || !(typeof value.$p0 === "number")) {
                __cope_m3_panic_0();
            }
            return;
        case "Flagged":
            if (Object.keys(value).length !== 3 || Object.getOwnPropertySymbols(value).length !== 0) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value, "$p0") || !(typeof value.$p0 === "string")) {
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
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
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
            if (!((__cope_m3_record_require_r1_6(value.$payload[0]), true))) { __cope_m3_panic_0(); }
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
            if (!((__cope_m3_validate_14(value.$payload[0]), true))) { __cope_m3_panic_0(); }
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
            if (!(typeof value.$payload[0] === "number")) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
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
            if (!((__cope_m3_result_validate_24(value.$payload[0]), true))) { __cope_m3_panic_0(); }
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
            if (!(typeof value.$payload[0] === "number")) { __cope_m3_panic_0(); }
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

function __cope_m3_table_require_t1_38(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_type_t1_36) || value[__cope_m3_table_type_t1_36] !== __cope_m3_table_type_t1_36 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_rows_t1_43) || typeof value[__cope_m3_table_rows_t1_43] !== "function" || Object.getOwnPropertySymbols(value).length !== 7) {
        __cope_m3_panic_0();
    }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630030_44)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630031_48)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630032_52)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630033_56)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630034_60)) { __cope_m3_panic_0(); }
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
    const __cope_m3_table_storage___cope_00740031002e00630030_46 = Object.freeze(["north", "south", "east", "west"]);
    const __cope_m3_table_column_value___cope_00740031002e00630030_47 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630030_47, {
        [__cope_m3_column_type_31]: { value: __cope_m3_column_type_31, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630030_45]: { value: __cope_m3_column_type___cope_00740031002e00630030_45, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_32]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_15, "err", [{ $type: __cope_m3_type_9, $tag: "InvalidIndex", $p0: index }]);
            }
            if (index < 0 || index >= 4) {
                return __cope_m3_make_2(__cope_m3_result_type_15, "err", [{ $type: __cope_m3_type_9, $tag: "OutOfBounds", $p0: index, $p1: 4 }]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_15, "ok", [__cope_m3_table_storage___cope_00740031002e00630030_46[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630030_47);
    const __cope_m3_table_storage___cope_00740031002e00630031_50 = Object.freeze([1, 2, 3, 4]);
    const __cope_m3_table_column_value___cope_00740031002e00630031_51 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630031_51, {
        [__cope_m3_column_type_31]: { value: __cope_m3_column_type_31, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630031_49]: { value: __cope_m3_column_type___cope_00740031002e00630031_49, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_32]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_17, "err", [{ $type: __cope_m3_type_9, $tag: "InvalidIndex", $p0: index }]);
            }
            if (index < 0 || index >= 4) {
                return __cope_m3_make_2(__cope_m3_result_type_17, "err", [{ $type: __cope_m3_type_9, $tag: "OutOfBounds", $p0: index, $p1: 4 }]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_17, "ok", [__cope_m3_table_storage___cope_00740031002e00630031_50[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630031_51);
    const __cope_m3_table_storage___cope_00740031002e00630032_54 = Object.freeze([__cope_m3_record_make_r1_5(1, 2), __cope_m3_record_make_r1_5(3, 4), __cope_m3_record_make_r1_5(5, 6), __cope_m3_record_make_r1_5(7, 8)]);
    const __cope_m3_table_column_value___cope_00740031002e00630032_55 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630032_55, {
        [__cope_m3_column_type_31]: { value: __cope_m3_column_type_31, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630032_53]: { value: __cope_m3_column_type___cope_00740031002e00630032_53, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_32]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_19, "err", [{ $type: __cope_m3_type_9, $tag: "InvalidIndex", $p0: index }]);
            }
            if (index < 0 || index >= 4) {
                return __cope_m3_make_2(__cope_m3_result_type_19, "err", [{ $type: __cope_m3_type_9, $tag: "OutOfBounds", $p0: index, $p1: 4 }]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_19, "ok", [__cope_m3_table_storage___cope_00740031002e00630032_54[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630032_55);
    const __cope_m3_table_storage___cope_00740031002e00630033_58 = Object.freeze([{ $type: __cope_m3_type_12, $tag: "Present", $p0: 10 }, { $type: __cope_m3_type_12, $tag: "Present", $p0: 20 }, __cope_m3_type_12_case_0, { $type: __cope_m3_type_12, $tag: "Flagged", $p0: "sensor" }]);
    const __cope_m3_table_column_value___cope_00740031002e00630033_59 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630033_59, {
        [__cope_m3_column_type_31]: { value: __cope_m3_column_type_31, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630033_57]: { value: __cope_m3_column_type___cope_00740031002e00630033_57, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_32]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_21, "err", [{ $type: __cope_m3_type_9, $tag: "InvalidIndex", $p0: index }]);
            }
            if (index < 0 || index >= 4) {
                return __cope_m3_make_2(__cope_m3_result_type_21, "err", [{ $type: __cope_m3_type_9, $tag: "OutOfBounds", $p0: index, $p1: 4 }]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_21, "ok", [__cope_m3_table_storage___cope_00740031002e00630033_58[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630033_59);
    const __cope_m3_table_storage___cope_00740031002e00630034_62 = Object.freeze([__cope_m3_make_2(__cope_m3_result_type_23, "ok", [10]), __cope_m3_make_2(__cope_m3_result_type_23, "ok", [20]), __cope_m3_make_2(__cope_m3_result_type_23, "err", ["missing"]), __cope_m3_make_2(__cope_m3_result_type_23, "err", ["flagged"])]);
    const __cope_m3_table_column_value___cope_00740031002e00630034_63 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630034_63, {
        [__cope_m3_column_type_31]: { value: __cope_m3_column_type_31, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630034_61]: { value: __cope_m3_column_type___cope_00740031002e00630034_61, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_32]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_25, "err", [{ $type: __cope_m3_type_9, $tag: "InvalidIndex", $p0: index }]);
            }
            if (index < 0 || index >= 4) {
                return __cope_m3_make_2(__cope_m3_result_type_25, "err", [{ $type: __cope_m3_type_9, $tag: "OutOfBounds", $p0: index, $p1: 4 }]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_25, "ok", [__cope_m3_table_storage___cope_00740031002e00630034_62[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630034_63);
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_table_type_t1_36]: { value: __cope_m3_table_type_t1_36, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_rows_t1_43]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_2(__cope_m3_result_type_27, "err", [{ $type: __cope_m3_type_9, $tag: "InvalidIndex", $p0: index }]);
            }
            if (index < 0 || index >= 4) {
                return __cope_m3_make_2(__cope_m3_result_type_27, "err", [{ $type: __cope_m3_type_9, $tag: "OutOfBounds", $p0: index, $p1: 4 }]);
            }
            return __cope_m3_make_2(__cope_m3_result_type_27, "ok", [__cope_m3_table_row_create_t1_41(value, index)]);
        }, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630030_44]: { value: __cope_m3_table_column_value___cope_00740031002e00630030_47, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630031_48]: { value: __cope_m3_table_column_value___cope_00740031002e00630031_51, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630032_52]: { value: __cope_m3_table_column_value___cope_00740031002e00630032_55, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630033_56]: { value: __cope_m3_table_column_value___cope_00740031002e00630033_59, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630034_60]: { value: __cope_m3_table_column_value___cope_00740031002e00630034_63, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(value);
}

const __cope_m3_table_value_t1_42 = __cope_m3_table_create_t1_40();

function stateValue(value) {
    return (() => { const __cope_m3_match_64 = value; switch (__cope_m3_match_64.$tag) { case "Missing": { return 0; } case "Present": { const reading = __cope_m3_match_64.$p0; return reading; } case "Flagged": { const reason = __cope_m3_match_64.$p0; return reason.length; } default: return __cope_m3_panic_0(); } })();
}

function acceptedValue(value) {
    const __cope_m3_result_match_65 = value;
    __cope_m3_result_validate_24(__cope_m3_result_match_65);
    let __cope_m3_result_value_66;
    switch (__cope_m3_result_match_65.$tag) {
        case "ok": {
            const reading = __cope_m3_result_match_65.$payload[0];
            __cope_m3_result_value_66 = reading;
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
    return __cope_m3_result_value_66;
}

function rowScore(index) {
    const __cope_m3_table_receiver_67 = __cope_m3_table_value_t1_42;
    const __cope_m3_table_index_68 = index;
    __cope_m3_table_require_t1_38(__cope_m3_table_receiver_67);
    const __cope_m3_table_row_69 = __cope_m3_table_receiver_67[__cope_m3_table_rows_t1_43](__cope_m3_table_index_68);
    __cope_m3_result_validate_28(__cope_m3_table_row_69);
    const __cope_m3_propagate_70 = __cope_m3_table_row_69;
    __cope_m3_result_validate_28(__cope_m3_propagate_70);
    if (__cope_m3_propagate_70.$tag === "err") {
        return __cope_m3_make_2(__cope_m3_result_type_29, "err", [__cope_m3_propagate_70.$payload[0]]);
    }
    const row = __cope_m3_propagate_70.$payload[0];
    const __cope_m3_table_row_71 = row;
    __cope_m3_table_row_require_t1_39(__cope_m3_table_row_71);
    const __cope_m3_row_table_72 = __cope_m3_table_row_71[__cope_m3_table_row_table_34];
    const __cope_m3_row_field_73 = __cope_m3_row_table_72[__cope_m3_table_column___cope_00740031002e00630032_52][__cope_m3_column_read_32](__cope_m3_table_row_71[__cope_m3_table_row_index_35]);
    __cope_m3_result_validate_20(__cope_m3_row_field_73);
    if (__cope_m3_row_field_73.$tag !== "ok") { __cope_m3_panic_0(); }
    const __cope_m3_record_receiver_74 = __cope_m3_row_field_73.$payload[0];
    const __cope_m3_ordered_79 = __cope_m3_record_receiver_74.$f0;
    const __cope_m3_table_row_75 = row;
    __cope_m3_table_row_require_t1_39(__cope_m3_table_row_75);
    const __cope_m3_row_table_76 = __cope_m3_table_row_75[__cope_m3_table_row_table_34];
    const __cope_m3_row_field_77 = __cope_m3_row_table_76[__cope_m3_table_column___cope_00740031002e00630032_52][__cope_m3_column_read_32](__cope_m3_table_row_75[__cope_m3_table_row_index_35]);
    __cope_m3_result_validate_20(__cope_m3_row_field_77);
    if (__cope_m3_row_field_77.$tag !== "ok") { __cope_m3_panic_0(); }
    const __cope_m3_record_receiver_78 = __cope_m3_row_field_77.$payload[0];
    const pointScore = (__cope_m3_ordered_79 + __cope_m3_record_receiver_78.$f1);
    const __cope_m3_table_row_80 = row;
    __cope_m3_table_row_require_t1_39(__cope_m3_table_row_80);
    const __cope_m3_row_table_81 = __cope_m3_table_row_80[__cope_m3_table_row_table_34];
    const __cope_m3_row_field_82 = __cope_m3_row_table_81[__cope_m3_table_column___cope_00740031002e00630031_48][__cope_m3_column_read_32](__cope_m3_table_row_80[__cope_m3_table_row_index_35]);
    __cope_m3_result_validate_18(__cope_m3_row_field_82);
    if (__cope_m3_row_field_82.$tag !== "ok") { __cope_m3_panic_0(); }
    const __cope_m3_ordered_86 = (__cope_m3_row_field_82.$payload[0] + pointScore);
    const __cope_m3_table_row_83 = row;
    __cope_m3_table_row_require_t1_39(__cope_m3_table_row_83);
    const __cope_m3_row_table_84 = __cope_m3_table_row_83[__cope_m3_table_row_table_34];
    const __cope_m3_row_field_85 = __cope_m3_row_table_84[__cope_m3_table_column___cope_00740031002e00630033_56][__cope_m3_column_read_32](__cope_m3_table_row_83[__cope_m3_table_row_index_35]);
    __cope_m3_result_validate_22(__cope_m3_row_field_85);
    if (__cope_m3_row_field_85.$tag !== "ok") { __cope_m3_panic_0(); }
    const __cope_m3_ordered_90 = (__cope_m3_ordered_86 + stateValue(__cope_m3_row_field_85.$payload[0]));
    const __cope_m3_table_row_87 = row;
    __cope_m3_table_row_require_t1_39(__cope_m3_table_row_87);
    const __cope_m3_row_table_88 = __cope_m3_table_row_87[__cope_m3_table_row_table_34];
    const __cope_m3_row_field_89 = __cope_m3_row_table_88[__cope_m3_table_column___cope_00740031002e00630034_60][__cope_m3_column_read_32](__cope_m3_table_row_87[__cope_m3_table_row_index_35]);
    __cope_m3_result_validate_26(__cope_m3_row_field_89);
    if (__cope_m3_row_field_89.$tag !== "ok") { __cope_m3_panic_0(); }
    return __cope_m3_make_2(__cope_m3_result_type_29, "ok", [(__cope_m3_ordered_90 + acceptedValue(__cope_m3_row_field_89.$payload[0]))]);
}

function columnScore() {
    let index = 0;
    let total = 0;
    while ((index < 4)) {
        const __cope_m3_ordered_96 = total;
        const __cope_m3_table_receiver_91 = __cope_m3_table_value_t1_42;
        __cope_m3_table_require_t1_38(__cope_m3_table_receiver_91);
        const __cope_m3_column_receiver_92 = __cope_m3_table_receiver_91[__cope_m3_table_column___cope_00740031002e00630031_48];
        const __cope_m3_column_index_93 = index;
        __cope_m3_column_require_33(__cope_m3_column_receiver_92);
        const __cope_m3_column_element_94 = __cope_m3_column_receiver_92[__cope_m3_column_read_32](__cope_m3_column_index_93);
        __cope_m3_result_validate_18(__cope_m3_column_element_94);
        const __cope_m3_unwrap_95 = __cope_m3_column_element_94;
        __cope_m3_result_validate_18(__cope_m3_unwrap_95);
        if (__cope_m3_unwrap_95.$tag === "err") {
            __cope_m3_panic_unwrap_1(__cope_m3_unwrap_95.$payload[0]);
        }
        const __cope_m3_ordered_103 = (__cope_m3_ordered_96 + __cope_m3_unwrap_95.$payload[0]);
        const __cope_m3_table_receiver_97 = __cope_m3_table_value_t1_42;
        __cope_m3_table_require_t1_38(__cope_m3_table_receiver_97);
        const __cope_m3_column_receiver_98 = __cope_m3_table_receiver_97[__cope_m3_table_column___cope_00740031002e00630032_52];
        const __cope_m3_column_index_99 = index;
        __cope_m3_column_require_33(__cope_m3_column_receiver_98);
        const __cope_m3_column_element_100 = __cope_m3_column_receiver_98[__cope_m3_column_read_32](__cope_m3_column_index_99);
        __cope_m3_result_validate_20(__cope_m3_column_element_100);
        const __cope_m3_unwrap_101 = __cope_m3_column_element_100;
        __cope_m3_result_validate_20(__cope_m3_unwrap_101);
        if (__cope_m3_unwrap_101.$tag === "err") {
            __cope_m3_panic_unwrap_1(__cope_m3_unwrap_101.$payload[0]);
        }
        const __cope_m3_record_receiver_102 = __cope_m3_unwrap_101.$payload[0];
        (total = (__cope_m3_ordered_103 + __cope_m3_record_receiver_102.$f0));
        (index = (index + 1));
    }
    return total;
}

function boundsScore(index) {
    const __cope_m3_table_receiver_104 = __cope_m3_table_value_t1_42;
    __cope_m3_table_require_t1_38(__cope_m3_table_receiver_104);
    const __cope_m3_column_receiver_105 = __cope_m3_table_receiver_104[__cope_m3_table_column___cope_00740031002e00630031_48];
    const __cope_m3_column_index_106 = index;
    __cope_m3_column_require_33(__cope_m3_column_receiver_105);
    const __cope_m3_column_element_107 = __cope_m3_column_receiver_105[__cope_m3_column_read_32](__cope_m3_column_index_106);
    __cope_m3_result_validate_18(__cope_m3_column_element_107);
    const __cope_m3_result_match_109 = __cope_m3_column_element_107;
    __cope_m3_result_validate_18(__cope_m3_result_match_109);
    let __cope_m3_result_value_110;
    switch (__cope_m3_result_match_109.$tag) {
        case "ok": {
            const value = __cope_m3_result_match_109.$payload[0];
            __cope_m3_result_value_110 = value;
            break;
        }
        case "err": {
            const error = __cope_m3_result_match_109.$payload[0];
            __cope_m3_result_value_110 = (() => { const __cope_m3_match_108 = error; switch (__cope_m3_match_108.$tag) { case "InvalidIndex": { const value = __cope_m3_match_108.$p0; return 100; } case "OutOfBounds": { const value = __cope_m3_match_108.$p0; const rowCount = __cope_m3_match_108.$p1; return rowCount; } default: return __cope_m3_panic_0(); } })();
            break;
        }
        default:
            __cope_m3_panic_0();
    }
    return __cope_m3_result_value_110;
}

function repeatedAccess() {
    const __cope_m3_table_receiver_111 = __cope_m3_table_value_t1_42;
    const __cope_m3_table_index_112 = 0;
    __cope_m3_table_require_t1_38(__cope_m3_table_receiver_111);
    const __cope_m3_table_row_113 = __cope_m3_table_receiver_111[__cope_m3_table_rows_t1_43](__cope_m3_table_index_112);
    __cope_m3_result_validate_28(__cope_m3_table_row_113);
    const __cope_m3_unwrap_114 = __cope_m3_table_row_113;
    __cope_m3_result_validate_28(__cope_m3_unwrap_114);
    if (__cope_m3_unwrap_114.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_114.$payload[0]);
    }
    const first = __cope_m3_unwrap_114.$payload[0];
    const __cope_m3_table_receiver_115 = __cope_m3_table_value_t1_42;
    const __cope_m3_table_index_116 = 1;
    __cope_m3_table_require_t1_38(__cope_m3_table_receiver_115);
    const __cope_m3_table_row_117 = __cope_m3_table_receiver_115[__cope_m3_table_rows_t1_43](__cope_m3_table_index_116);
    __cope_m3_result_validate_28(__cope_m3_table_row_117);
    const __cope_m3_unwrap_118 = __cope_m3_table_row_117;
    __cope_m3_result_validate_28(__cope_m3_unwrap_118);
    if (__cope_m3_unwrap_118.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_118.$payload[0]);
    }
    const second = __cope_m3_unwrap_118.$payload[0];
    const __cope_m3_table_receiver_119 = __cope_m3_table_value_t1_42;
    const __cope_m3_table_index_120 = 2;
    __cope_m3_table_require_t1_38(__cope_m3_table_receiver_119);
    const __cope_m3_table_row_121 = __cope_m3_table_receiver_119[__cope_m3_table_rows_t1_43](__cope_m3_table_index_120);
    __cope_m3_result_validate_28(__cope_m3_table_row_121);
    const __cope_m3_unwrap_122 = __cope_m3_table_row_121;
    __cope_m3_result_validate_28(__cope_m3_unwrap_122);
    if (__cope_m3_unwrap_122.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_122.$payload[0]);
    }
    const third = __cope_m3_unwrap_122.$payload[0];
    const __cope_m3_table_receiver_123 = __cope_m3_table_value_t1_42;
    const __cope_m3_table_index_124 = 3;
    __cope_m3_table_require_t1_38(__cope_m3_table_receiver_123);
    const __cope_m3_table_row_125 = __cope_m3_table_receiver_123[__cope_m3_table_rows_t1_43](__cope_m3_table_index_124);
    __cope_m3_result_validate_28(__cope_m3_table_row_125);
    const __cope_m3_unwrap_126 = __cope_m3_table_row_125;
    __cope_m3_result_validate_28(__cope_m3_unwrap_126);
    if (__cope_m3_unwrap_126.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_126.$payload[0]);
    }
    const fourth = __cope_m3_unwrap_126.$payload[0];
    const __cope_m3_table_row_127 = first;
    __cope_m3_table_row_require_t1_39(__cope_m3_table_row_127);
    const __cope_m3_row_table_128 = __cope_m3_table_row_127[__cope_m3_table_row_table_34];
    const __cope_m3_row_field_129 = __cope_m3_row_table_128[__cope_m3_table_column___cope_00740031002e00630032_52][__cope_m3_column_read_32](__cope_m3_table_row_127[__cope_m3_table_row_index_35]);
    __cope_m3_result_validate_20(__cope_m3_row_field_129);
    if (__cope_m3_row_field_129.$tag !== "ok") { __cope_m3_panic_0(); }
    const __cope_m3_record_receiver_130 = __cope_m3_row_field_129.$payload[0];
    const __cope_m3_ordered_135 = __cope_m3_record_receiver_130.$f0;
    const __cope_m3_table_row_131 = second;
    __cope_m3_table_row_require_t1_39(__cope_m3_table_row_131);
    const __cope_m3_row_table_132 = __cope_m3_table_row_131[__cope_m3_table_row_table_34];
    const __cope_m3_row_field_133 = __cope_m3_row_table_132[__cope_m3_table_column___cope_00740031002e00630032_52][__cope_m3_column_read_32](__cope_m3_table_row_131[__cope_m3_table_row_index_35]);
    __cope_m3_result_validate_20(__cope_m3_row_field_133);
    if (__cope_m3_row_field_133.$tag !== "ok") { __cope_m3_panic_0(); }
    const __cope_m3_record_receiver_134 = __cope_m3_row_field_133.$payload[0];
    const __cope_m3_ordered_140 = (__cope_m3_ordered_135 + __cope_m3_record_receiver_134.$f0);
    const __cope_m3_table_row_136 = third;
    __cope_m3_table_row_require_t1_39(__cope_m3_table_row_136);
    const __cope_m3_row_table_137 = __cope_m3_table_row_136[__cope_m3_table_row_table_34];
    const __cope_m3_row_field_138 = __cope_m3_row_table_137[__cope_m3_table_column___cope_00740031002e00630032_52][__cope_m3_column_read_32](__cope_m3_table_row_136[__cope_m3_table_row_index_35]);
    __cope_m3_result_validate_20(__cope_m3_row_field_138);
    if (__cope_m3_row_field_138.$tag !== "ok") { __cope_m3_panic_0(); }
    const __cope_m3_record_receiver_139 = __cope_m3_row_field_138.$payload[0];
    const __cope_m3_ordered_145 = (__cope_m3_ordered_140 + __cope_m3_record_receiver_139.$f0);
    const __cope_m3_table_row_141 = fourth;
    __cope_m3_table_row_require_t1_39(__cope_m3_table_row_141);
    const __cope_m3_row_table_142 = __cope_m3_table_row_141[__cope_m3_table_row_table_34];
    const __cope_m3_row_field_143 = __cope_m3_row_table_142[__cope_m3_table_column___cope_00740031002e00630032_52][__cope_m3_column_read_32](__cope_m3_table_row_141[__cope_m3_table_row_index_35]);
    __cope_m3_result_validate_20(__cope_m3_row_field_143);
    if (__cope_m3_row_field_143.$tag !== "ok") { __cope_m3_panic_0(); }
    const __cope_m3_record_receiver_144 = __cope_m3_row_field_143.$payload[0];
    const __cope_m3_ordered_149 = (__cope_m3_ordered_145 + __cope_m3_record_receiver_144.$f0);
    const __cope_m3_table_row_146 = first;
    __cope_m3_table_row_require_t1_39(__cope_m3_table_row_146);
    const __cope_m3_row_table_147 = __cope_m3_table_row_146[__cope_m3_table_row_table_34];
    const __cope_m3_row_field_148 = __cope_m3_row_table_147[__cope_m3_table_column___cope_00740031002e00630033_56][__cope_m3_column_read_32](__cope_m3_table_row_146[__cope_m3_table_row_index_35]);
    __cope_m3_result_validate_22(__cope_m3_row_field_148);
    if (__cope_m3_row_field_148.$tag !== "ok") { __cope_m3_panic_0(); }
    const __cope_m3_ordered_153 = (__cope_m3_ordered_149 + stateValue(__cope_m3_row_field_148.$payload[0]));
    const __cope_m3_table_row_150 = second;
    __cope_m3_table_row_require_t1_39(__cope_m3_table_row_150);
    const __cope_m3_row_table_151 = __cope_m3_table_row_150[__cope_m3_table_row_table_34];
    const __cope_m3_row_field_152 = __cope_m3_row_table_151[__cope_m3_table_column___cope_00740031002e00630033_56][__cope_m3_column_read_32](__cope_m3_table_row_150[__cope_m3_table_row_index_35]);
    __cope_m3_result_validate_22(__cope_m3_row_field_152);
    if (__cope_m3_row_field_152.$tag !== "ok") { __cope_m3_panic_0(); }
    const __cope_m3_ordered_157 = (__cope_m3_ordered_153 + stateValue(__cope_m3_row_field_152.$payload[0]));
    const __cope_m3_table_row_154 = third;
    __cope_m3_table_row_require_t1_39(__cope_m3_table_row_154);
    const __cope_m3_row_table_155 = __cope_m3_table_row_154[__cope_m3_table_row_table_34];
    const __cope_m3_row_field_156 = __cope_m3_row_table_155[__cope_m3_table_column___cope_00740031002e00630033_56][__cope_m3_column_read_32](__cope_m3_table_row_154[__cope_m3_table_row_index_35]);
    __cope_m3_result_validate_22(__cope_m3_row_field_156);
    if (__cope_m3_row_field_156.$tag !== "ok") { __cope_m3_panic_0(); }
    const __cope_m3_ordered_161 = (__cope_m3_ordered_157 + stateValue(__cope_m3_row_field_156.$payload[0]));
    const __cope_m3_table_row_158 = fourth;
    __cope_m3_table_row_require_t1_39(__cope_m3_table_row_158);
    const __cope_m3_row_table_159 = __cope_m3_table_row_158[__cope_m3_table_row_table_34];
    const __cope_m3_row_field_160 = __cope_m3_row_table_159[__cope_m3_table_column___cope_00740031002e00630033_56][__cope_m3_column_read_32](__cope_m3_table_row_158[__cope_m3_table_row_index_35]);
    __cope_m3_result_validate_22(__cope_m3_row_field_160);
    if (__cope_m3_row_field_160.$tag !== "ok") { __cope_m3_panic_0(); }
    return (__cope_m3_ordered_161 + stateValue(__cope_m3_row_field_160.$payload[0]));
}

function main() {
    const __cope_m3_unwrap_162 = rowScore(0);
    __cope_m3_result_validate_30(__cope_m3_unwrap_162);
    if (__cope_m3_unwrap_162.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_162.$payload[0]);
    }
    const __cope_m3_ordered_164 = __cope_m3_unwrap_162.$payload[0];
    const __cope_m3_unwrap_163 = rowScore(1);
    __cope_m3_result_validate_30(__cope_m3_unwrap_163);
    if (__cope_m3_unwrap_163.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_163.$payload[0]);
    }
    const __cope_m3_ordered_166 = (__cope_m3_ordered_164 + __cope_m3_unwrap_163.$payload[0]);
    const __cope_m3_unwrap_165 = rowScore(2);
    __cope_m3_result_validate_30(__cope_m3_unwrap_165);
    if (__cope_m3_unwrap_165.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_165.$payload[0]);
    }
    const __cope_m3_ordered_168 = (__cope_m3_ordered_166 + __cope_m3_unwrap_165.$payload[0]);
    const __cope_m3_unwrap_167 = rowScore(3);
    __cope_m3_result_validate_30(__cope_m3_unwrap_167);
    if (__cope_m3_unwrap_167.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_167.$payload[0]);
    }
    return ((((__cope_m3_ordered_168 + __cope_m3_unwrap_167.$payload[0]) + columnScore()) + boundsScore(99)) + repeatedAccess());
}
