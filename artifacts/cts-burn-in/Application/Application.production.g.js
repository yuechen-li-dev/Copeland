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

const __cope_m3_record_type_r2_9 = Symbol("r2");

function __cope_m3_record_make_r2_11(field0, field1) {
    return { [__cope_m3_record_type_r2_9]: __cope_m3_record_type_r2_9, $f0: field0, $f1: field1 };
}

function __cope_m3_record_require_r2_12(value) {
    if (typeof value !== "object" || value === null || value[__cope_m3_record_type_r2_9] !== __cope_m3_record_type_r2_9 || Object.keys(value).length !== 2 || Object.getOwnPropertySymbols(value).length !== 1 || !Object.prototype.hasOwnProperty.call(value, "$f0") || !Object.prototype.hasOwnProperty.call(value, "$f1")) {
        __cope_m3_panic_0();
    }
    if (!(typeof value.$f0 === "number")) { __cope_m3_panic_0(); }
    if (!(typeof value.$f1 === "number")) { __cope_m3_panic_0(); }
}

const __cope_m3_record_type_r3_15 = Symbol("r3");

function __cope_m3_record_make_r3_17(field0, field1, field2) {
    return { [__cope_m3_record_type_r3_15]: __cope_m3_record_type_r3_15, $f0: field0, $f1: field1, $f2: field2 };
}

function __cope_m3_record_require_r3_18(value) {
    if (typeof value !== "object" || value === null || value[__cope_m3_record_type_r3_15] !== __cope_m3_record_type_r3_15 || Object.keys(value).length !== 3 || Object.getOwnPropertySymbols(value).length !== 1 || !Object.prototype.hasOwnProperty.call(value, "$f0") || !Object.prototype.hasOwnProperty.call(value, "$f1") || !Object.prototype.hasOwnProperty.call(value, "$f2")) {
        __cope_m3_panic_0();
    }
    if (!(typeof value.$f0 === "string")) { __cope_m3_panic_0(); }
    if (!(typeof value.$f1 === "number")) { __cope_m3_panic_0(); }
    if (!(typeof value.$f2 === "number")) { __cope_m3_panic_0(); }
}

const __cope_m3_record_type_r6_22 = Symbol("r6");

function __cope_m3_record_make_r6_24(field0, field1, field2) {
    return { [__cope_m3_record_type_r6_22]: __cope_m3_record_type_r6_22, $f0: field0, $f1: field1, $f2: field2 };
}

function __cope_m3_record_require_r6_25(value) {
    if (typeof value !== "object" || value === null || value[__cope_m3_record_type_r6_22] !== __cope_m3_record_type_r6_22 || Object.keys(value).length !== 3 || Object.getOwnPropertySymbols(value).length !== 1 || !Object.prototype.hasOwnProperty.call(value, "$f0") || !Object.prototype.hasOwnProperty.call(value, "$f1") || !Object.prototype.hasOwnProperty.call(value, "$f2")) {
        __cope_m3_panic_0();
    }
    if (!(typeof value.$f0 === "string")) { __cope_m3_panic_0(); }
    if (!(typeof value.$f1 === "string")) { __cope_m3_panic_0(); }
    if (!(typeof value.$f2 === "number")) { __cope_m3_panic_0(); }
}

const __cope_m3_record_type_r4_29 = Symbol("r4");

function __cope_m3_record_make_r4_31(field0, field1, field2) {
    return { [__cope_m3_record_type_r4_29]: __cope_m3_record_type_r4_29, $f0: field0, $f1: field1, $f2: field2 };
}

function __cope_m3_record_require_r4_32(value) {
    if (typeof value !== "object" || value === null || value[__cope_m3_record_type_r4_29] !== __cope_m3_record_type_r4_29 || Object.keys(value).length !== 3 || Object.getOwnPropertySymbols(value).length !== 1 || !Object.prototype.hasOwnProperty.call(value, "$f0") || !Object.prototype.hasOwnProperty.call(value, "$f1") || !Object.prototype.hasOwnProperty.call(value, "$f2")) {
        __cope_m3_panic_0();
    }
    if (!(typeof value.$f0 === "string")) { __cope_m3_panic_0(); }
    if (!(typeof value.$f1 === "string")) { __cope_m3_panic_0(); }
    if (!((__cope_m3_record_require_r1_6(value.$f2), true))) { __cope_m3_panic_0(); }
}

const __cope_m3_record_type_r5_36 = Symbol("r5");

function __cope_m3_record_make_r5_38(field0, field1, field2, field3, field4) {
    return { [__cope_m3_record_type_r5_36]: __cope_m3_record_type_r5_36, $f0: field0, $f1: field1, $f2: field2, $f3: field3, $f4: field4 };
}

function __cope_m3_record_require_r5_39(value) {
    if (typeof value !== "object" || value === null || value[__cope_m3_record_type_r5_36] !== __cope_m3_record_type_r5_36 || Object.keys(value).length !== 5 || Object.getOwnPropertySymbols(value).length !== 1 || !Object.prototype.hasOwnProperty.call(value, "$f0") || !Object.prototype.hasOwnProperty.call(value, "$f1") || !Object.prototype.hasOwnProperty.call(value, "$f2") || !Object.prototype.hasOwnProperty.call(value, "$f3") || !Object.prototype.hasOwnProperty.call(value, "$f4")) {
        __cope_m3_panic_0();
    }
    if (!(typeof value.$f0 === "number")) { __cope_m3_panic_0(); }
    if (!((__cope_m3_record_require_r4_32(value.$f1), true))) { __cope_m3_panic_0(); }
    if (!(Array.isArray(value.$f2))) { __cope_m3_panic_0(); }
    if (!((__cope_m3_record_require_r6_25(value.$f3), true))) { __cope_m3_panic_0(); }
    if (!((__cope_m3_validate_71(value.$f4), true))) { __cope_m3_panic_0(); }
}

const __cope_m3_record_type_r7_45 = Symbol("r7");

function __cope_m3_record_make_r7_47(field0, field1) {
    return { [__cope_m3_record_type_r7_45]: __cope_m3_record_type_r7_45, $f0: field0, $f1: field1 };
}

function __cope_m3_record_require_r7_48(value) {
    if (typeof value !== "object" || value === null || value[__cope_m3_record_type_r7_45] !== __cope_m3_record_type_r7_45 || Object.keys(value).length !== 2 || Object.getOwnPropertySymbols(value).length !== 1 || !Object.prototype.hasOwnProperty.call(value, "$f0") || !Object.prototype.hasOwnProperty.call(value, "$f1")) {
        __cope_m3_panic_0();
    }
    if (!(typeof value.$f0 === "number")) { __cope_m3_panic_0(); }
    if (!(typeof value.$f1 === "number")) { __cope_m3_panic_0(); }
}

const __cope_m3_record_type_r8_51 = Symbol("r8");

function __cope_m3_record_make_r8_53(field0, field1) {
    return { [__cope_m3_record_type_r8_51]: __cope_m3_record_type_r8_51, $f0: field0, $f1: field1 };
}

function __cope_m3_record_require_r8_54(value) {
    if (typeof value !== "object" || value === null || value[__cope_m3_record_type_r8_51] !== __cope_m3_record_type_r8_51 || Object.keys(value).length !== 2 || Object.getOwnPropertySymbols(value).length !== 1 || !Object.prototype.hasOwnProperty.call(value, "$f0") || !Object.prototype.hasOwnProperty.call(value, "$f1")) {
        __cope_m3_panic_0();
    }
    if (!(typeof value.$f0 === "string")) { __cope_m3_panic_0(); }
    if (!((__cope_m3_record_require_r7_48(value.$f1), true))) { __cope_m3_panic_0(); }
}

const __cope_m3_record_type_r9_57 = Symbol("r9");

function __cope_m3_record_make_r9_59(field0, field1) {
    return { [__cope_m3_record_type_r9_57]: __cope_m3_record_type_r9_57, $f0: field0, $f1: field1 };
}

function __cope_m3_record_require_r9_60(value) {
    if (typeof value !== "object" || value === null || value[__cope_m3_record_type_r9_57] !== __cope_m3_record_type_r9_57 || Object.keys(value).length !== 2 || Object.getOwnPropertySymbols(value).length !== 1 || !Object.prototype.hasOwnProperty.call(value, "$f0") || !Object.prototype.hasOwnProperty.call(value, "$f1")) {
        __cope_m3_panic_0();
    }
    if (!(typeof value.$f0 === "number")) { __cope_m3_panic_0(); }
    if (!(typeof value.$f1 === "number")) { __cope_m3_panic_0(); }
}

const __cope_m3_record_type_r10_63 = Symbol("r10");

function __cope_m3_record_make_r10_65(field0, field1) {
    return { [__cope_m3_record_type_r10_63]: __cope_m3_record_type_r10_63, $f0: field0, $f1: field1 };
}

function __cope_m3_record_require_r10_66(value) {
    if (typeof value !== "object" || value === null || value[__cope_m3_record_type_r10_63] !== __cope_m3_record_type_r10_63 || Object.keys(value).length !== 2 || Object.getOwnPropertySymbols(value).length !== 1 || !Object.prototype.hasOwnProperty.call(value, "$f0") || !Object.prototype.hasOwnProperty.call(value, "$f1")) {
        __cope_m3_panic_0();
    }
    if (!(typeof value.$f0 === "number")) { __cope_m3_panic_0(); }
    if (!(typeof value.$f1 === "number")) { __cope_m3_panic_0(); }
}

const __cope_m3_type_69 = Symbol("Fulfillment");
const __cope_m3_type_69_case_0 = Object.freeze({ $type: __cope_m3_type_69, $tag: "Pending" });

function __cope_m3_validate_71(value) {
    if (typeof value !== "object" || value === null || value.$type !== __cope_m3_type_69 || typeof value.$tag !== "string") {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Pending":
            if (Object.keys(value).length !== 2 || Object.getOwnPropertySymbols(value).length !== 0) {
                __cope_m3_panic_0();
            }
            return;
        case "Packed":
            if (Object.keys(value).length !== 3 || Object.getOwnPropertySymbols(value).length !== 0) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value, "$p0") || !(typeof value.$p0 === "number")) {
                __cope_m3_panic_0();
            }
            return;
        case "Shipped":
            if (Object.keys(value).length !== 4 || Object.getOwnPropertySymbols(value).length !== 0) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value, "$p0") || !(typeof value.$p0 === "string")) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value, "$p1") || !((__cope_m3_record_require_r1_6(value.$p1), true))) {
                __cope_m3_panic_0();
            }
            return;
        case "Rejected":
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

const __cope_m3_result_type_72 = Object.freeze(Object.create(null));

const __cope_m3_result_type_74 = Object.freeze(Object.create(null));

const __cope_m3_result_type_76 = Object.freeze(Object.create(null));

const __cope_m3_result_type_78 = Object.freeze(Object.create(null));

function __cope_m3_result_validate_73(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_72 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_record_require_r6_25(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_75(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_74 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
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

function __cope_m3_result_validate_77(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_76 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_record_require_r3_18(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function __cope_m3_result_validate_79(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_78 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!((__cope_m3_record_require_r2_12(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function Parcel__constructor(code, weight) {
    if ((weight < 0)) {
        return __cope_m3_make_2(__cope_m3_result_type_72, "err", ["weight must be nonnegative"]);
    }
    const __cope_m3_record_init_80 = code;
    const __cope_m3_record_init_81 = Parcel__normalize(code);
    const __cope_m3_record_init_82 = weight;
    return __cope_m3_make_2(__cope_m3_result_type_72, "ok", [__cope_m3_record_make_r6_24(__cope_m3_record_init_80, __cope_m3_record_init_81, __cope_m3_record_init_82)]);
}

function Parcel__normalize(code) {
    return code;
}

function Parcel__adjust(parcel, delta) {
    const __cope_m3_record_source_83 = parcel;
    const __cope_m3_record_receiver_84 = parcel;
    const __cope_m3_record_replacement_85 = (__cope_m3_record_receiver_84.$f2 + delta);
    return __cope_m3_record_make_r6_24(__cope_m3_record_source_83.$f0, __cope_m3_record_source_83.$f1, __cope_m3_record_replacement_85);
}

function lineTotal(line) {
    const __cope_m3_record_receiver_86 = line;
    const __cope_m3_ordered_88 = __cope_m3_record_receiver_86.$f1;
    const __cope_m3_record_receiver_87 = line;
    return (__cope_m3_ordered_88 * __cope_m3_record_receiver_87.$f2);
}

function orderTotal(order) {
    let total = 0;
    const __cope_m3_record_receiver_89 = order;
    for (const line of __cope_m3_record_receiver_89.$f2) {
        (total = (total + lineTotal(line)));
    }
    return total;
}

function validateQuantity(quantity) {
    if ((quantity < 1)) {
        return __cope_m3_make_2(__cope_m3_result_type_74, "err", ["quantity must be positive"]);
    }
    return __cope_m3_make_2(__cope_m3_result_type_74, "ok", [quantity]);
}

function reserve(line) {
    const __cope_m3_record_receiver_90 = line;
    const __cope_m3_propagate_91 = validateQuantity(__cope_m3_record_receiver_90.$f1);
    __cope_m3_result_validate_75(__cope_m3_propagate_91);
    if (__cope_m3_propagate_91.$tag === "err") {
        return __cope_m3_make_2(__cope_m3_result_type_76, "err", [__cope_m3_propagate_91.$payload[0]]);
    }
    const quantity = __cope_m3_propagate_91.$payload[0];
    const __cope_m3_record_source_92 = line;
    const __cope_m3_record_replacement_93 = quantity;
    return __cope_m3_make_2(__cope_m3_result_type_76, "ok", [__cope_m3_record_make_r3_17(__cope_m3_record_source_92.$f0, __cope_m3_record_replacement_93, __cope_m3_record_source_92.$f2)]);
}

function describe(status) {
    return (() => { const __cope_m3_match_94 = status; switch (__cope_m3_match_94.$tag) { case "Pending": { return "pending"; } case "Packed": { const attempts = __cope_m3_match_94.$p0; return "packed"; } case "Shipped": { const tracking = __cope_m3_match_94.$p0; const position = __cope_m3_match_94.$p1; return tracking; } case "Rejected": { const reason = __cope_m3_match_94.$p0; return reason; } default: return __cope_m3_panic_0(); } })();
}

function identity__record_copeland_inferred_record_ordered_4_name_16_primitive_string_8_position_83_record_copeland_inferred_record_ordered_1_x_13_primitive_int_1_y_13_primitive_int______24A9BE5A43158CFC(value) {
    return value;
}

function moveLocalItem() {
    const __cope_m3_record_init_95 = "sample";
    const __cope_m3_record_init_96 = 1;
    const __cope_m3_record_init_97 = 2;
    const __cope_m3_record_init_98 = __cope_m3_record_make_r7_47(__cope_m3_record_init_96, __cope_m3_record_init_97);
    const item = __cope_m3_record_make_r8_53(__cope_m3_record_init_95, __cope_m3_record_init_98);
    const __cope_m3_record_init_99 = "peer";
    const __cope_m3_record_init_100 = 3;
    const __cope_m3_record_init_101 = 4;
    const __cope_m3_record_init_102 = __cope_m3_record_make_r7_47(__cope_m3_record_init_100, __cope_m3_record_init_101);
    const peer = identity__record_copeland_inferred_record_ordered_4_name_16_primitive_string_8_position_83_record_copeland_inferred_record_ordered_1_x_13_primitive_int_1_y_13_primitive_int______24A9BE5A43158CFC(__cope_m3_record_make_r8_53(__cope_m3_record_init_99, __cope_m3_record_init_102));
    const __cope_m3_record_source_103 = item;
    const __cope_m3_record_receiver_104 = item;
    const __cope_m3_record_source_105 = __cope_m3_record_receiver_104.$f1;
    const __cope_m3_record_receiver_106 = peer;
    const __cope_m3_record_receiver_107 = __cope_m3_record_receiver_106.$f1;
    const __cope_m3_record_replacement_108 = __cope_m3_record_receiver_107.$f0;
    const __cope_m3_record_replacement_109 = __cope_m3_record_make_r7_47(__cope_m3_record_replacement_108, __cope_m3_record_source_105.$f1);
    const moved = __cope_m3_record_make_r8_53(__cope_m3_record_source_103.$f0, __cope_m3_record_replacement_109);
    const __cope_m3_record_receiver_110 = moved;
    const __cope_m3_record_receiver_111 = __cope_m3_record_receiver_110.$f1;
    const __cope_m3_ordered_114 = __cope_m3_record_receiver_111.$f0;
    const __cope_m3_record_receiver_112 = moved;
    const __cope_m3_record_receiver_113 = __cope_m3_record_receiver_112.$f1;
    return (__cope_m3_ordered_114 + __cope_m3_record_receiver_113.$f1);
}

function orderedShapePressure() {
    const __cope_m3_record_init_115 = 1;
    const __cope_m3_record_init_116 = 2;
    const a = __cope_m3_record_make_r7_47(__cope_m3_record_init_115, __cope_m3_record_init_116);
    const __cope_m3_record_init_117 = 3;
    const __cope_m3_record_init_118 = 4;
    const b = __cope_m3_record_make_r7_47(__cope_m3_record_init_117, __cope_m3_record_init_118);
    const __cope_m3_record_init_119 = 4;
    const __cope_m3_record_init_120 = 3;
    const c = __cope_m3_record_make_r9_59(__cope_m3_record_init_119, __cope_m3_record_init_120);
    const __cope_m3_record_receiver_121 = a;
    const __cope_m3_ordered_123 = __cope_m3_record_receiver_121.$f0;
    const __cope_m3_record_receiver_122 = b;
    const __cope_m3_ordered_125 = (__cope_m3_ordered_123 + __cope_m3_record_receiver_122.$f1);
    const __cope_m3_record_receiver_124 = c;
    return (__cope_m3_ordered_125 + __cope_m3_record_receiver_124.$f1);
}

function makeLocalResult(valid) {
    if (valid) {
        const __cope_m3_record_init_126 = 20;
        const __cope_m3_record_init_127 = 22;
        return __cope_m3_make_2(__cope_m3_result_type_78, "ok", [__cope_m3_record_make_r2_11(__cope_m3_record_init_126, __cope_m3_record_init_127)]);
    }
    return __cope_m3_make_2(__cope_m3_result_type_78, "err", ["missing"]);
}

function readLocalResult() {
    const __cope_m3_result_match_131 = makeLocalResult(true);
    __cope_m3_result_validate_79(__cope_m3_result_match_131);
    let __cope_m3_result_value_132;
    switch (__cope_m3_result_match_131.$tag) {
        case "ok": {
            const value = __cope_m3_result_match_131.$payload[0];
            const __cope_m3_record_receiver_128 = value;
            const __cope_m3_ordered_130 = __cope_m3_record_receiver_128.$f0;
            const __cope_m3_record_receiver_129 = value;
            __cope_m3_result_value_132 = (__cope_m3_ordered_130 + __cope_m3_record_receiver_129.$f1);
            break;
        }
        case "err": {
            const error = __cope_m3_result_match_131.$payload[0];
            __cope_m3_result_value_132 = 0;
            break;
        }
        default:
            __cope_m3_panic_0();
    }
    return __cope_m3_result_value_132;
}

function evaluationTrace(buffer, value) {
    (() => { const __cope_m3_mutable_array_135 = buffer; const __cope_m3_array_index_136 = 0; if (__cope_m3_array_index_136 < 0 || __cope_m3_array_index_136 >= __cope_m3_mutable_array_135.length) throw new RangeError("Copeland array index is out of bounds."); __cope_m3_mutable_array_135[__cope_m3_array_index_136] = (((() => { const __cope_m3_mutable_array_133 = buffer; const __cope_m3_array_index_134 = 0; if (__cope_m3_array_index_134 < 0 || __cope_m3_array_index_134 >= __cope_m3_mutable_array_133.length) throw new RangeError("Copeland array index is out of bounds."); return __cope_m3_mutable_array_133[__cope_m3_array_index_134]; })() * 10) + value); return (((() => { const __cope_m3_mutable_array_133 = buffer; const __cope_m3_array_index_134 = 0; if (__cope_m3_array_index_134 < 0 || __cope_m3_array_index_134 >= __cope_m3_mutable_array_133.length) throw new RangeError("Copeland array index is out of bounds."); return __cope_m3_mutable_array_133[__cope_m3_array_index_134]; })() * 10) + value); })();
    return value;
}

function evaluationOrder() {
    const buffer = (() => { const __cope_m3_array_length_137 = 1; if (__cope_m3_array_length_137 < 0) throw new RangeError("Copeland mutable array length cannot be negative."); return Array(__cope_m3_array_length_137).fill(0); })();
    const __cope_m3_record_init_138 = evaluationTrace(buffer, 1);
    const __cope_m3_record_init_139 = evaluationTrace(buffer, 2);
    const value = __cope_m3_record_make_r10_65(__cope_m3_record_init_138, __cope_m3_record_init_139);
    const __cope_m3_record_source_140 = value;
    const __cope_m3_record_replacement_141 = evaluationTrace(buffer, 3);
    const __cope_m3_record_replacement_142 = evaluationTrace(buffer, 4);
    const moved = __cope_m3_record_make_r10_65(__cope_m3_record_replacement_141, __cope_m3_record_replacement_142);
    const __cope_m3_ordered_146 = (() => { const __cope_m3_mutable_array_143 = buffer; const __cope_m3_array_index_144 = 0; if (__cope_m3_array_index_144 < 0 || __cope_m3_array_index_144 >= __cope_m3_mutable_array_143.length) throw new RangeError("Copeland array index is out of bounds."); return __cope_m3_mutable_array_143[__cope_m3_array_index_144]; })();
    const __cope_m3_record_receiver_145 = moved;
    const __cope_m3_ordered_148 = (__cope_m3_ordered_146 + __cope_m3_record_receiver_145.$f0);
    const __cope_m3_record_receiver_147 = moved;
    return (__cope_m3_ordered_148 + __cope_m3_record_receiver_147.$f1);
}

function coordinateTotal__record_Position__D9FBFA8340216B5C(value) {
    const __cope_m3_record_receiver_149 = value;
    const __cope_m3_ordered_151 = __cope_m3_record_receiver_149.$f0;
    const __cope_m3_record_receiver_150 = value;
    return (__cope_m3_ordered_151 + __cope_m3_record_receiver_150.$f1);
}

function Parcel__identity__primitive_number__FDC3F3A4758964FC(value) {
    return value;
}

function publicWeight__record_Parcel__8EF3EF46C3C17BEA(value) {
    const __cope_m3_record_receiver_152 = value;
    return __cope_m3_record_receiver_152.$f2;
}

function main() {
    const __cope_m3_record_init_153 = "Ada";
    const __cope_m3_record_init_154 = "west";
    const __cope_m3_record_init_155 = 5;
    const __cope_m3_record_init_156 = 8;
    const __cope_m3_record_init_157 = __cope_m3_record_make_r1_5(__cope_m3_record_init_155, __cope_m3_record_init_156);
    const customer = __cope_m3_record_make_r4_31(__cope_m3_record_init_153, __cope_m3_record_init_154, __cope_m3_record_init_157);
    const __cope_m3_record_init_158 = "A";
    const __cope_m3_record_init_159 = 2;
    const __cope_m3_record_init_160 = 10.5;
    const first = __cope_m3_record_make_r3_17(__cope_m3_record_init_158, __cope_m3_record_init_159, __cope_m3_record_init_160);
    const __cope_m3_record_init_161 = "B";
    const __cope_m3_record_init_162 = 1;
    const __cope_m3_record_init_163 = 4;
    const second = __cope_m3_record_make_r3_17(__cope_m3_record_init_161, __cope_m3_record_init_162, __cope_m3_record_init_163);
    const __cope_m3_unwrap_164 = Parcel__constructor("PKG-7", 10);
    __cope_m3_result_validate_73(__cope_m3_unwrap_164);
    if (__cope_m3_unwrap_164.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_164.$payload[0]);
    }
    const parcel = __cope_m3_unwrap_164.$payload[0];
    const adjusted = Parcel__adjust(parcel, 2);
    const __cope_m3_record_init_165 = 7;
    const __cope_m3_record_init_166 = customer;
    const __cope_m3_record_init_167 = [first, second];
    const __cope_m3_record_init_168 = adjusted;
    const __cope_m3_record_init_169 = { $type: __cope_m3_type_69, $tag: "Packed", $p0: 1 };
    const order = __cope_m3_record_make_r5_38(__cope_m3_record_init_165, __cope_m3_record_init_166, __cope_m3_record_init_167, __cope_m3_record_init_168, __cope_m3_record_init_169);
    const __cope_m3_unwrap_170 = reserve(first);
    __cope_m3_result_validate_77(__cope_m3_unwrap_170);
    if (__cope_m3_unwrap_170.$tag === "err") {
        __cope_m3_panic_unwrap_1(__cope_m3_unwrap_170.$payload[0]);
    }
    const reserved = __cope_m3_unwrap_170.$payload[0];
    const __cope_m3_record_receiver_171 = order;
    const status = describe(__cope_m3_record_receiver_171.$f4);
    const __cope_m3_record_init_172 = 5;
    const __cope_m3_record_init_173 = 8;
    const namedBoundary = coordinateTotal__record_Position__D9FBFA8340216B5C(__cope_m3_record_make_r1_5(__cope_m3_record_init_172, __cope_m3_record_init_173));
    const __cope_m3_ordered_175 = orderTotal(order);
    const __cope_m3_record_receiver_174 = reserved;
    return ((((((((__cope_m3_ordered_175 + __cope_m3_record_receiver_174.$f1) + namedBoundary) + moveLocalItem()) + orderedShapePressure()) + readLocalResult()) + evaluationOrder()) + status.length) + Parcel__identity__primitive_number__FDC3F3A4758964FC(publicWeight__record_Parcel__8EF3EF46C3C17BEA(adjusted)));
}
