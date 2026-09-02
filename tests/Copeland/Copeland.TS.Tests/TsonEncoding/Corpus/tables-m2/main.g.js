"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_make_1(type, tag, payload) {
    const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
    if (type === __cope_m3_type_8) __cope_m3_instances_9.add(value);
    if (type === __cope_m3_type_11) __cope_m3_instances_12.add(value);
    if (type === __cope_m3_type_14) __cope_m3_instances_15.add(value);
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

const __cope_m3_type_14 = Object.freeze(Object.create(null));
const __cope_m3_instances_15 = new WeakSet();

function __cope_m3_validate_10(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_9.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_8 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "InvalidUnicode":
            if (value.$payload.length !== 0) {
                __cope_m3_panic_0();
            }
            return;
        case "OutputLimitExceeded":
            if (value.$payload.length !== 0) {
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

function __cope_m3_validate_16(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_15.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_14 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Off":
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

const __cope_m3_result_type_17 = Object.freeze(Object.create(null));

const __cope_m3_result_type_19 = Object.freeze(Object.create(null));

const __cope_m3_result_type_21 = Object.freeze(Object.create(null));

const __cope_m3_result_type_23 = Object.freeze(Object.create(null));

const __cope_m3_result_type_25 = Object.freeze(Object.create(null));

const __cope_m3_result_type_27 = Object.freeze(Object.create(null));

const __cope_m3_result_type_29 = Object.freeze(Object.create(null));

const __cope_m3_result_type_31 = Object.freeze(Object.create(null));

const __cope_m3_result_type_33 = Object.freeze(Object.create(null));

const __cope_m3_column_type_35 = Symbol("cope.column");
const __cope_m3_column_read_36 = Symbol("cope.column.read");
const __cope_m3_column_instances_37 = new WeakSet();
const __cope_m3_column_values_38 = Symbol("cope.column.values");
const __cope_m3_table_row_table_40 = Symbol("cope.table.row.table");
const __cope_m3_table_row_index_41 = Symbol("cope.table.row.index");

function __cope_m3_column_require_39(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_column_type_35) || value[__cope_m3_column_type_35] !== __cope_m3_column_type_35 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_column_read_36) || typeof value[__cope_m3_column_read_36] !== "function" || !__cope_m3_column_instances_37.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_column_values_38) || !Array.isArray(value[__cope_m3_column_values_38]) || !Object.isFrozen(value[__cope_m3_column_values_38])) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_table_type_t1_42 = Symbol("t1");
const __cope_m3_table_instances_t1_43 = new WeakSet();
const __cope_m3_table_row_type_t1_44 = Symbol("t1.row");
const __cope_m3_table_rows_t1_50 = Symbol("t1.rows");
const __cope_m3_table_column___cope_00740031002e00630030_51 = Symbol("t1.c0");
const __cope_m3_column_type___cope_00740031002e00630030_52 = Symbol("t1.c0.column");
const __cope_m3_table_column___cope_00740031002e00630031_55 = Symbol("t1.c1");
const __cope_m3_column_type___cope_00740031002e00630031_56 = Symbol("t1.c1.column");

function __cope_m3_table_require_t1_45(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_table_instances_t1_43.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_type_t1_42) || value[__cope_m3_table_type_t1_42] !== __cope_m3_table_type_t1_42 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_rows_t1_50) || typeof value[__cope_m3_table_rows_t1_50] !== "function") {
        __cope_m3_panic_0();
    }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630030_51)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740031002e00630031_55)) { __cope_m3_panic_0(); }
}

function __cope_m3_table_row_require_t1_46(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_type_t1_44) || value[__cope_m3_table_row_type_t1_44] !== __cope_m3_table_row_type_t1_44 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_table_40) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_index_41) || !Number.isInteger(value[__cope_m3_table_row_index_41])) {
        __cope_m3_panic_0();
    }
    __cope_m3_table_require_t1_45(value[__cope_m3_table_row_table_40]);
}

function __cope_m3_table_row_create_t1_48(tableValue, index) {
    const row = Object.create(null);
    Object.defineProperties(row, {
        [__cope_m3_table_row_type_t1_44]: { value: __cope_m3_table_row_type_t1_44, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_table_40]: { value: tableValue, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_index_41]: { value: index, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(row);
}

function __cope_m3_table_create_t1_47() {
    const __cope_m3_table_storage___cope_00740031002e00630030_53 = Object.freeze([]);
    const __cope_m3_table_column_value___cope_00740031002e00630030_54 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630030_54, {
        [__cope_m3_column_type_35]: { value: __cope_m3_column_type_35, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630030_52]: { value: __cope_m3_column_type___cope_00740031002e00630030_52, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_values_38]: { value: __cope_m3_table_storage___cope_00740031002e00630030_53, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_36]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_1(__cope_m3_result_type_17, "err", [__cope_m3_make_1(__cope_m3_type_11, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 0) {
                return __cope_m3_make_1(__cope_m3_result_type_17, "err", [__cope_m3_make_1(__cope_m3_type_11, "OutOfBounds", [index, 0])]);
            }
            return __cope_m3_make_1(__cope_m3_result_type_17, "ok", [__cope_m3_table_storage___cope_00740031002e00630030_53[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630030_54);
    __cope_m3_column_instances_37.add(__cope_m3_table_column_value___cope_00740031002e00630030_54);
    const __cope_m3_table_storage___cope_00740031002e00630031_57 = Object.freeze([]);
    const __cope_m3_table_column_value___cope_00740031002e00630031_58 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740031002e00630031_58, {
        [__cope_m3_column_type_35]: { value: __cope_m3_column_type_35, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740031002e00630031_56]: { value: __cope_m3_column_type___cope_00740031002e00630031_56, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_values_38]: { value: __cope_m3_table_storage___cope_00740031002e00630031_57, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_36]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_1(__cope_m3_result_type_19, "err", [__cope_m3_make_1(__cope_m3_type_11, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 0) {
                return __cope_m3_make_1(__cope_m3_result_type_19, "err", [__cope_m3_make_1(__cope_m3_type_11, "OutOfBounds", [index, 0])]);
            }
            return __cope_m3_make_1(__cope_m3_result_type_19, "ok", [__cope_m3_table_storage___cope_00740031002e00630031_57[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740031002e00630031_58);
    __cope_m3_column_instances_37.add(__cope_m3_table_column_value___cope_00740031002e00630031_58);
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_table_type_t1_42]: { value: __cope_m3_table_type_t1_42, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_rows_t1_50]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_1(__cope_m3_result_type_21, "err", [__cope_m3_make_1(__cope_m3_type_11, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 0) {
                return __cope_m3_make_1(__cope_m3_result_type_21, "err", [__cope_m3_make_1(__cope_m3_type_11, "OutOfBounds", [index, 0])]);
            }
            return __cope_m3_make_1(__cope_m3_result_type_21, "ok", [__cope_m3_table_row_create_t1_48(value, index)]);
        }, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630030_51]: { value: __cope_m3_table_column_value___cope_00740031002e00630030_54, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740031002e00630031_55]: { value: __cope_m3_table_column_value___cope_00740031002e00630031_58, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_table_instances_t1_43.add(value);
    return value;
}

const __cope_m3_table_value_t1_49 = __cope_m3_table_create_t1_47();

const __cope_m3_table_type_t2_59 = Symbol("t2");
const __cope_m3_table_instances_t2_60 = new WeakSet();
const __cope_m3_table_row_type_t2_61 = Symbol("t2.row");
const __cope_m3_table_rows_t2_67 = Symbol("t2.rows");
const __cope_m3_table_column___cope_00740032002e00630030_68 = Symbol("t2.c0");
const __cope_m3_column_type___cope_00740032002e00630030_69 = Symbol("t2.c0.column");
const __cope_m3_table_column___cope_00740032002e00630031_72 = Symbol("t2.c1");
const __cope_m3_column_type___cope_00740032002e00630031_73 = Symbol("t2.c1.column");
const __cope_m3_table_column___cope_00740032002e00630032_76 = Symbol("t2.c2");
const __cope_m3_column_type___cope_00740032002e00630032_77 = Symbol("t2.c2.column");
const __cope_m3_table_column___cope_00740032002e00630033_80 = Symbol("t2.c3");
const __cope_m3_column_type___cope_00740032002e00630033_81 = Symbol("t2.c3.column");
const __cope_m3_table_column___cope_00740032002e00630034_84 = Symbol("t2.c4");
const __cope_m3_column_type___cope_00740032002e00630034_85 = Symbol("t2.c4.column");

function __cope_m3_table_require_t2_62(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_table_instances_t2_60.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_type_t2_59) || value[__cope_m3_table_type_t2_59] !== __cope_m3_table_type_t2_59 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_rows_t2_67) || typeof value[__cope_m3_table_rows_t2_67] !== "function") {
        __cope_m3_panic_0();
    }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740032002e00630030_68)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740032002e00630031_72)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740032002e00630032_76)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740032002e00630033_80)) { __cope_m3_panic_0(); }
    if (!Object.prototype.hasOwnProperty.call(value, __cope_m3_table_column___cope_00740032002e00630034_84)) { __cope_m3_panic_0(); }
}

function __cope_m3_table_row_require_t2_63(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_type_t2_61) || value[__cope_m3_table_row_type_t2_61] !== __cope_m3_table_row_type_t2_61 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_table_40) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_table_row_index_41) || !Number.isInteger(value[__cope_m3_table_row_index_41])) {
        __cope_m3_panic_0();
    }
    __cope_m3_table_require_t2_62(value[__cope_m3_table_row_table_40]);
}

function __cope_m3_table_row_create_t2_65(tableValue, index) {
    const row = Object.create(null);
    Object.defineProperties(row, {
        [__cope_m3_table_row_type_t2_61]: { value: __cope_m3_table_row_type_t2_61, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_table_40]: { value: tableValue, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_row_index_41]: { value: index, writable: false, enumerable: false, configurable: false },
    });
    return Object.freeze(row);
}

function __cope_m3_table_create_t2_64() {
    const __cope_m3_table_storage___cope_00740032002e00630030_70 = Object.freeze([true, false, true, false, true]);
    const __cope_m3_table_column_value___cope_00740032002e00630030_71 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740032002e00630030_71, {
        [__cope_m3_column_type_35]: { value: __cope_m3_column_type_35, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740032002e00630030_69]: { value: __cope_m3_column_type___cope_00740032002e00630030_69, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_values_38]: { value: __cope_m3_table_storage___cope_00740032002e00630030_70, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_36]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_1(__cope_m3_result_type_17, "err", [__cope_m3_make_1(__cope_m3_type_11, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 5) {
                return __cope_m3_make_1(__cope_m3_result_type_17, "err", [__cope_m3_make_1(__cope_m3_type_11, "OutOfBounds", [index, 5])]);
            }
            return __cope_m3_make_1(__cope_m3_result_type_17, "ok", [__cope_m3_table_storage___cope_00740032002e00630030_70[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740032002e00630030_71);
    __cope_m3_column_instances_37.add(__cope_m3_table_column_value___cope_00740032002e00630030_71);
    const __cope_m3_table_storage___cope_00740032002e00630031_74 = Object.freeze([0, -0, 1.5, NaN, -Infinity]);
    const __cope_m3_table_column_value___cope_00740032002e00630031_75 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740032002e00630031_75, {
        [__cope_m3_column_type_35]: { value: __cope_m3_column_type_35, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740032002e00630031_73]: { value: __cope_m3_column_type___cope_00740032002e00630031_73, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_values_38]: { value: __cope_m3_table_storage___cope_00740032002e00630031_74, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_36]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_1(__cope_m3_result_type_23, "err", [__cope_m3_make_1(__cope_m3_type_11, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 5) {
                return __cope_m3_make_1(__cope_m3_result_type_23, "err", [__cope_m3_make_1(__cope_m3_type_11, "OutOfBounds", [index, 5])]);
            }
            return __cope_m3_make_1(__cope_m3_result_type_23, "ok", [__cope_m3_table_storage___cope_00740032002e00630031_74[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740032002e00630031_75);
    __cope_m3_column_instances_37.add(__cope_m3_table_column_value___cope_00740032002e00630031_75);
    const __cope_m3_table_storage___cope_00740032002e00630032_78 = Object.freeze([__cope_m3_record_make_r1_5("plain"), __cope_m3_record_make_r1_5("quote \" slash \\ newline\n"), __cope_m3_record_make_r1_5("雪"), __cope_m3_record_make_r1_5("\ud83d\ude00"), __cope_m3_record_make_r1_5("\ud801\udc37")]);
    const __cope_m3_table_column_value___cope_00740032002e00630032_79 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740032002e00630032_79, {
        [__cope_m3_column_type_35]: { value: __cope_m3_column_type_35, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740032002e00630032_77]: { value: __cope_m3_column_type___cope_00740032002e00630032_77, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_values_38]: { value: __cope_m3_table_storage___cope_00740032002e00630032_78, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_36]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_1(__cope_m3_result_type_25, "err", [__cope_m3_make_1(__cope_m3_type_11, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 5) {
                return __cope_m3_make_1(__cope_m3_result_type_25, "err", [__cope_m3_make_1(__cope_m3_type_11, "OutOfBounds", [index, 5])]);
            }
            return __cope_m3_make_1(__cope_m3_result_type_25, "ok", [__cope_m3_table_storage___cope_00740032002e00630032_78[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740032002e00630032_79);
    __cope_m3_column_instances_37.add(__cope_m3_table_column_value___cope_00740032002e00630032_79);
    const __cope_m3_table_storage___cope_00740032002e00630033_82 = Object.freeze([__cope_m3_make_1(__cope_m3_type_14, "Off", []), __cope_m3_make_1(__cope_m3_type_14, "Named", ["payload"]), __cope_m3_make_1(__cope_m3_type_14, "Named", ["雪"]), __cope_m3_make_1(__cope_m3_type_14, "Off", []), __cope_m3_make_1(__cope_m3_type_14, "Named", ["array"])]);
    const __cope_m3_table_column_value___cope_00740032002e00630033_83 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740032002e00630033_83, {
        [__cope_m3_column_type_35]: { value: __cope_m3_column_type_35, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740032002e00630033_81]: { value: __cope_m3_column_type___cope_00740032002e00630033_81, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_values_38]: { value: __cope_m3_table_storage___cope_00740032002e00630033_82, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_36]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_1(__cope_m3_result_type_27, "err", [__cope_m3_make_1(__cope_m3_type_11, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 5) {
                return __cope_m3_make_1(__cope_m3_result_type_27, "err", [__cope_m3_make_1(__cope_m3_type_11, "OutOfBounds", [index, 5])]);
            }
            return __cope_m3_make_1(__cope_m3_result_type_27, "ok", [__cope_m3_table_storage___cope_00740032002e00630033_82[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740032002e00630033_83);
    __cope_m3_column_instances_37.add(__cope_m3_table_column_value___cope_00740032002e00630033_83);
    const __cope_m3_table_storage___cope_00740032002e00630034_86 = Object.freeze([Object.freeze([]), Object.freeze([Object.freeze([1, 2]), Object.freeze([])]), Object.freeze([Object.freeze([]), Object.freeze([0])]), Object.freeze([Object.freeze([Infinity])]), Object.freeze([Object.freeze([NaN])])]);
    const __cope_m3_table_column_value___cope_00740032002e00630034_87 = Object.create(null);
    Object.defineProperties(__cope_m3_table_column_value___cope_00740032002e00630034_87, {
        [__cope_m3_column_type_35]: { value: __cope_m3_column_type_35, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_type___cope_00740032002e00630034_85]: { value: __cope_m3_column_type___cope_00740032002e00630034_85, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_values_38]: { value: __cope_m3_table_storage___cope_00740032002e00630034_86, writable: false, enumerable: false, configurable: false },
        [__cope_m3_column_read_36]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_1(__cope_m3_result_type_29, "err", [__cope_m3_make_1(__cope_m3_type_11, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 5) {
                return __cope_m3_make_1(__cope_m3_result_type_29, "err", [__cope_m3_make_1(__cope_m3_type_11, "OutOfBounds", [index, 5])]);
            }
            return __cope_m3_make_1(__cope_m3_result_type_29, "ok", [__cope_m3_table_storage___cope_00740032002e00630034_86[index]]);
        }, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(__cope_m3_table_column_value___cope_00740032002e00630034_87);
    __cope_m3_column_instances_37.add(__cope_m3_table_column_value___cope_00740032002e00630034_87);
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_table_type_t2_59]: { value: __cope_m3_table_type_t2_59, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_rows_t2_67]: { value: (index) => {
            if (!Number.isFinite(index) || !Number.isInteger(index)) {
                return __cope_m3_make_1(__cope_m3_result_type_31, "err", [__cope_m3_make_1(__cope_m3_type_11, "InvalidIndex", [index])]);
            }
            if (index < 0 || index >= 5) {
                return __cope_m3_make_1(__cope_m3_result_type_31, "err", [__cope_m3_make_1(__cope_m3_type_11, "OutOfBounds", [index, 5])]);
            }
            return __cope_m3_make_1(__cope_m3_result_type_31, "ok", [__cope_m3_table_row_create_t2_65(value, index)]);
        }, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740032002e00630030_68]: { value: __cope_m3_table_column_value___cope_00740032002e00630030_71, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740032002e00630031_72]: { value: __cope_m3_table_column_value___cope_00740032002e00630031_75, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740032002e00630032_76]: { value: __cope_m3_table_column_value___cope_00740032002e00630032_79, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740032002e00630033_80]: { value: __cope_m3_table_column_value___cope_00740032002e00630033_83, writable: false, enumerable: false, configurable: false },
        [__cope_m3_table_column___cope_00740032002e00630034_84]: { value: __cope_m3_table_column_value___cope_00740032002e00630034_87, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_table_instances_t2_60.add(value);
    return value;
}

const __cope_m3_table_value_t2_66 = __cope_m3_table_create_t2_64();

const __cope_m3_tson_2 = (() => {
    function makeWriter(maximumBytes, maximumStringCodeUnits) {
        const parts = [];
        const bitsBuffer = new ArrayBuffer(8);
        const bitsView = new DataView(bitsBuffer);
        let byteCount = 0;
        let error = null;
        function fail(kind) { if (error === null) error = kind; return false; }
        function appendRaw(value) {
            let added = 0;
            for (let index = 0; index < value.length; index += 1) {
                const code = value.charCodeAt(index);
                if (code <= 0x7F) added += 1;
                else if (code <= 0x7FF) added += 2;
                else if (code >= 0xD800 && code <= 0xDBFF) {
                    if (index + 1 >= value.length) return fail("invalid");
                    const low = value.charCodeAt(index + 1);
                    if (low < 0xDC00 || low > 0xDFFF) return fail("invalid");
                    added += 4;
                    index += 1;
                } else if (code >= 0xDC00 && code <= 0xDFFF) return fail("invalid");
                else added += 3;
            }
            if (byteCount > maximumBytes - added) return fail("limit");
            byteCount += added;
            parts.push(value);
            return true;
        }
        function unicodeEscape(code) { return appendRaw("\\u" + code.toString(16).toUpperCase().padStart(4, "0")); }
        function writeString(value) {
            if (value.length > maximumStringCodeUnits) return fail("limit");
            for (let index = 0; index < value.length; index += 1) {
                const code = value.charCodeAt(index);
                if (code >= 0xD800 && code <= 0xDBFF) {
                    if (index + 1 >= value.length) return fail("invalid");
                    const low = value.charCodeAt(index + 1);
                    if (low < 0xDC00 || low > 0xDFFF) return fail("invalid");
                    index += 1;
                } else if (code >= 0xDC00 && code <= 0xDFFF) return fail("invalid");
            }
            if (!appendRaw("\"")) return false;
            for (let index = 0; index < value.length; index += 1) {
                const code = value.charCodeAt(index);
                if (code === 0x22) { if (!appendRaw("\\\"")) return false; }
                else if (code === 0x5C) { if (!appendRaw("\\\\")) return false; }
                else if (code === 0x08) { if (!appendRaw("\\b")) return false; }
                else if (code === 0x0C) { if (!appendRaw("\\f")) return false; }
                else if (code === 0x0A) { if (!appendRaw("\\n")) return false; }
                else if (code === 0x0D) { if (!appendRaw("\\r")) return false; }
                else if (code === 0x09) { if (!appendRaw("\\t")) return false; }
                else if (code < 0x20 || code === 0x2028 || code === 0x2029) { if (!unicodeEscape(code)) return false; }
                else if (code >= 0xD800 && code <= 0xDBFF) {
                    if (!appendRaw(value.slice(index, index + 2))) return false;
                    index += 1;
                } else if (!appendRaw(value[index])) return false;
            }
            return appendRaw("\"");
        }
        function writeNumber(value) {
            bitsView.setFloat64(0, value, false);
            let high = bitsView.getUint32(0, false);
            let low = bitsView.getUint32(4, false);
            if ((high & 0x7FF00000) === 0x7FF00000 && ((high & 0x000FFFFF) !== 0 || low !== 0)) { high = 0x7FF80000; low = 0; }
            const hexadecimal = high.toString(16).toUpperCase().padStart(8, "0") + low.toString(16).toUpperCase().padStart(8, "0");
            return appendRaw("$number(\"" + hexadecimal + "\")");
        }
        return Object.freeze({
            static: appendRaw,
            indent: level => appendRaw(" ".repeat(level * 4)),
            string: writeString,
            number: writeNumber,
            outputLimit: () => fail("limit"),
            error: () => error,
            finish: () => parts.join(""),
        });
    }

    function writeBoolean(writer, value, indentation) { return writer.static(value ? "true" : "false"); }
    function writeNumber(writer, value, indentation) { return writer.number(value); }
    function writeString(writer, value, indentation) { return writer.string(value); }

    function writeP0R0(writer, value, indentation) {
        __cope_m3_record_require_r1_6(value);
        if (!writer.static("$record.Point({\n")) return false;
        if (!writer.indent(indentation + 1)) return false;
        if (!writer.static("\"name\": ")) return false;
        if (!writeString(writer, value[__cope_m3_record_field___cope_00720031002e00660030_7], indentation + 1)) return false;
        if (!writer.static(",\n")) return false;
        if (!writer.indent(indentation)) return false;
        return writer.static("})");
    }
    function writeP0E0(writer, value, indentation) {
        __cope_m3_validate_16(value);
        switch (value.$tag) {
            case "Off":
                return writer.static("State.Off");
            case "Named":
                if (!writer.static("State.Named(\n")) return false;
                if (!writer.indent(indentation + 1)) return false;
                if (!writeString(writer, value.$payload[0], indentation + 1)) return false;
                if (!writer.static("\n")) return false;
                if (!writer.indent(indentation)) return false;
                return writer.static(")");
            default:
                __cope_m3_panic_0();
        }
    }
    function writeP0A0(writer, value, indentation) {
        const array = value;
        if (!Array.isArray(array)) { __cope_m3_panic_0(); }
        const length = array.length;
        if (length > 100000) return writer.outputLimit();
        if (length === 0) return writer.static("[]");
        if (!writer.static("[\n")) return false;
        for (let index = 0; index < length; index += 1) {
            if (!Object.prototype.hasOwnProperty.call(array, index)) { __cope_m3_panic_0(); }
            const element = array[index];
            if (!Array.isArray(element)) { __cope_m3_panic_0(); }
            if (!writer.indent(indentation + 1)) return false;
            if (!writeP0A1(writer, element, indentation + 1)) return false;
            if (!writer.static(",\n")) return false;
        }
        if (!writer.indent(indentation)) return false;
        return writer.static("]");
    }
    function writeP0A1(writer, value, indentation) {
        const array = value;
        if (!Array.isArray(array)) { __cope_m3_panic_0(); }
        const length = array.length;
        if (length > 100000) return writer.outputLimit();
        if (length === 0) return writer.static("[]");
        if (!writer.static("[\n")) return false;
        for (let index = 0; index < length; index += 1) {
            if (!Object.prototype.hasOwnProperty.call(array, index)) { __cope_m3_panic_0(); }
            const element = array[index];
            if (typeof element !== "number") { __cope_m3_panic_0(); }
            if (!writer.indent(indentation + 1)) return false;
            if (!writeNumber(writer, element, indentation + 1)) return false;
            if (!writer.static(",\n")) return false;
        }
        if (!writer.indent(indentation)) return false;
        return writer.static("]");
    }
    function encode0(value) {
        __cope_m3_table_require_t2_62(value);
        const column0 = value[__cope_m3_table_column___cope_00740032002e00630030_68];
        __cope_m3_column_require_39(column0);
        if (column0[__cope_m3_column_type___cope_00740032002e00630030_69] !== __cope_m3_column_type___cope_00740032002e00630030_69) { __cope_m3_panic_0(); }
        const cells0 = column0[__cope_m3_column_values_38];
        const length0 = cells0.length;
        if (length0 !== 5) { __cope_m3_panic_0(); }
        const column1 = value[__cope_m3_table_column___cope_00740032002e00630031_72];
        __cope_m3_column_require_39(column1);
        if (column1[__cope_m3_column_type___cope_00740032002e00630031_73] !== __cope_m3_column_type___cope_00740032002e00630031_73) { __cope_m3_panic_0(); }
        const cells1 = column1[__cope_m3_column_values_38];
        const length1 = cells1.length;
        if (length1 !== 5) { __cope_m3_panic_0(); }
        const column2 = value[__cope_m3_table_column___cope_00740032002e00630032_76];
        __cope_m3_column_require_39(column2);
        if (column2[__cope_m3_column_type___cope_00740032002e00630032_77] !== __cope_m3_column_type___cope_00740032002e00630032_77) { __cope_m3_panic_0(); }
        const cells2 = column2[__cope_m3_column_values_38];
        const length2 = cells2.length;
        if (length2 !== 5) { __cope_m3_panic_0(); }
        const column3 = value[__cope_m3_table_column___cope_00740032002e00630033_80];
        __cope_m3_column_require_39(column3);
        if (column3[__cope_m3_column_type___cope_00740032002e00630033_81] !== __cope_m3_column_type___cope_00740032002e00630033_81) { __cope_m3_panic_0(); }
        const cells3 = column3[__cope_m3_column_values_38];
        const length3 = cells3.length;
        if (length3 !== 5) { __cope_m3_panic_0(); }
        const column4 = value[__cope_m3_table_column___cope_00740032002e00630034_84];
        __cope_m3_column_require_39(column4);
        if (column4[__cope_m3_column_type___cope_00740032002e00630034_85] !== __cope_m3_column_type___cope_00740032002e00630034_85) { __cope_m3_panic_0(); }
        const cells4 = column4[__cope_m3_column_values_38];
        const length4 = cells4.length;
        if (length4 !== 5) { __cope_m3_panic_0(); }
        const writer = makeWriter(1048576, 262144);
        if (!writer.static("const $schema: string = \"copeland://corpus/runtime-table-encoding\";\n\nrecord Point {\n    name: string;\n}\n\nrecord table Samples {\n")) {
            const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
            const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
            return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
        }
        if (length0 === 0) {
            if (!writer.static("    active: boolean = ") || !writer.static("[];\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
        } else {
            if (!writer.static("    active: boolean = ") || !writer.static("[\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
            for (let index = 0; index < length0; index += 1) {
                if (!Object.prototype.hasOwnProperty.call(cells0, index)) { __cope_m3_panic_0(); }
                const cell = cells0[index];
                if (!writer.indent(2) || !writeBoolean(writer, cell, 2) || !writer.static(",\n")) {
                    const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                    const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                    return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
                }
            }
            if (!writer.indent(1) || !writer.static("];\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
        }
        if (length1 === 0) {
            if (!writer.static("    score: number = ") || !writer.static("[];\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
        } else {
            if (!writer.static("    score: number = ") || !writer.static("[\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
            for (let index = 0; index < length1; index += 1) {
                if (!Object.prototype.hasOwnProperty.call(cells1, index)) { __cope_m3_panic_0(); }
                const cell = cells1[index];
                if (!writer.indent(2) || !writeNumber(writer, cell, 2) || !writer.static(",\n")) {
                    const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                    const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                    return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
                }
            }
            if (!writer.indent(1) || !writer.static("];\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
        }
        if (length2 === 0) {
            if (!writer.static("    point: Point = ") || !writer.static("[];\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
        } else {
            if (!writer.static("    point: Point = ") || !writer.static("[\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
            for (let index = 0; index < length2; index += 1) {
                if (!Object.prototype.hasOwnProperty.call(cells2, index)) { __cope_m3_panic_0(); }
                const cell = cells2[index];
                if (!writer.indent(2) || !writeP0R0(writer, cell, 2) || !writer.static(",\n")) {
                    const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                    const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                    return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
                }
            }
            if (!writer.indent(1) || !writer.static("];\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
        }
        if (length3 === 0) {
            if (!writer.static("    state: State = ") || !writer.static("[];\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
        } else {
            if (!writer.static("    state: State = ") || !writer.static("[\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
            for (let index = 0; index < length3; index += 1) {
                if (!Object.prototype.hasOwnProperty.call(cells3, index)) { __cope_m3_panic_0(); }
                const cell = cells3[index];
                if (!writer.indent(2) || !writeP0E0(writer, cell, 2) || !writer.static(",\n")) {
                    const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                    const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                    return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
                }
            }
            if (!writer.indent(1) || !writer.static("];\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
        }
        if (length4 === 0) {
            if (!writer.static("    values: number[][] = ") || !writer.static("[];\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
        } else {
            if (!writer.static("    values: number[][] = ") || !writer.static("[\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
            for (let index = 0; index < length4; index += 1) {
                if (!Object.prototype.hasOwnProperty.call(cells4, index)) { __cope_m3_panic_0(); }
                const cell = cells4[index];
                if (!writer.indent(2) || !writeP0A0(writer, cell, 2) || !writer.static(",\n")) {
                    const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                    const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                    return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
                }
            }
            if (!writer.indent(1) || !writer.static("];\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
        }
        if (!writer.static("}\n\nenum State {\n    Off,\n    Named(label: string),\n}\n\nconst $value = Samples;\n")) {
            const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
            const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
            return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
        }
        return __cope_m3_make_1(__cope_m3_result_type_33, "ok", [writer.finish()]);
    }

    function encode1(value) {
        __cope_m3_table_require_t1_45(value);
        const column0 = value[__cope_m3_table_column___cope_00740031002e00630030_51];
        __cope_m3_column_require_39(column0);
        if (column0[__cope_m3_column_type___cope_00740031002e00630030_52] !== __cope_m3_column_type___cope_00740031002e00630030_52) { __cope_m3_panic_0(); }
        const cells0 = column0[__cope_m3_column_values_38];
        const length0 = cells0.length;
        if (length0 !== 0) { __cope_m3_panic_0(); }
        const column1 = value[__cope_m3_table_column___cope_00740031002e00630031_55];
        __cope_m3_column_require_39(column1);
        if (column1[__cope_m3_column_type___cope_00740031002e00630031_56] !== __cope_m3_column_type___cope_00740031002e00630031_56) { __cope_m3_panic_0(); }
        const cells1 = column1[__cope_m3_column_values_38];
        const length1 = cells1.length;
        if (length1 !== 0) { __cope_m3_panic_0(); }
        const writer = makeWriter(1048576, 262144);
        if (!writer.static("const $schema: string = \"copeland://corpus/runtime-table-encoding\";\n\nrecord table Empty {\n")) {
            const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
            const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
            return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
        }
        if (length0 === 0) {
            if (!writer.static("    active: boolean = ") || !writer.static("[];\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
        } else {
            if (!writer.static("    active: boolean = ") || !writer.static("[\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
            for (let index = 0; index < length0; index += 1) {
                if (!Object.prototype.hasOwnProperty.call(cells0, index)) { __cope_m3_panic_0(); }
                const cell = cells0[index];
                if (!writer.indent(2) || !writeBoolean(writer, cell, 2) || !writer.static(",\n")) {
                    const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                    const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                    return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
                }
            }
            if (!writer.indent(1) || !writer.static("];\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
        }
        if (length1 === 0) {
            if (!writer.static("    note: string = ") || !writer.static("[];\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
        } else {
            if (!writer.static("    note: string = ") || !writer.static("[\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
            for (let index = 0; index < length1; index += 1) {
                if (!Object.prototype.hasOwnProperty.call(cells1, index)) { __cope_m3_panic_0(); }
                const cell = cells1[index];
                if (!writer.indent(2) || !writeString(writer, cell, 2) || !writer.static(",\n")) {
                    const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                    const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                    return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
                }
            }
            if (!writer.indent(1) || !writer.static("];\n")) {
                const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
                const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
                return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
            }
        }
        if (!writer.static("}\n\nconst $value = Empty;\n")) {
            const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
            const error = __cope_m3_make_1(__cope_m3_type_8, tag, []);
            return __cope_m3_make_1(__cope_m3_result_type_33, "err", [error]);
        }
        return __cope_m3_make_1(__cope_m3_result_type_33, "ok", [writer.finish()]);
    }

    const api = Object.create(null);
    Object.defineProperty(api, "tson0", { value: encode0, writable: false, enumerable: false, configurable: false });
    Object.defineProperty(api, "tson1", { value: encode1, writable: false, enumerable: false, configurable: false });
    return Object.freeze(api);
})();

function encode() {
    return __cope_m3_tson_2["tson0"](__cope_m3_table_value_t2_66);
}

function encodeEmpty() {
    return __cope_m3_tson_2["tson1"](__cope_m3_table_value_t1_49);
}
