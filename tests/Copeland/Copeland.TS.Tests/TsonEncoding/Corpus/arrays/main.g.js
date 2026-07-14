"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_make_1(type, tag, payload) {
    const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
    if (type === __cope_m3_type_20) __cope_m3_instances_21.add(value);
    if (type === __cope_m3_type_23) __cope_m3_instances_24.add(value);
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

const __cope_m3_record_type_r2_8 = Symbol("r2");
const __cope_m3_record_instances_r2_9 = new WeakSet();
const __cope_m3_record_field___cope_00720032002e00660030_12 = Symbol("r2.f0");
const __cope_m3_record_field___cope_00720032002e00660031_13 = Symbol("r2.f1");
const __cope_m3_record_field___cope_00720032002e00660032_14 = Symbol("r2.f2");
const __cope_m3_record_field___cope_00720032002e00660033_15 = Symbol("r2.f3");
const __cope_m3_record_field___cope_00720032002e00660034_16 = Symbol("r2.f4");
const __cope_m3_record_field___cope_00720032002e00660035_17 = Symbol("r2.f5");
const __cope_m3_record_field___cope_00720032002e00660036_18 = Symbol("r2.f6");
const __cope_m3_record_field___cope_00720032002e00660037_19 = Symbol("r2.f7");

function __cope_m3_record_make_r2_10(field0, field1, field2, field3, field4, field5, field6, field7) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r2_8]: { value: __cope_m3_record_type_r2_8, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660030_12]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660031_13]: { value: field1, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660032_14]: { value: field2, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660033_15]: { value: field3, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660034_16]: { value: field4, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660035_17]: { value: field5, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660036_18]: { value: field6, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660037_19]: { value: field7, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r2_9.add(value);
    return value;
}

function __cope_m3_record_require_r2_11(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r2_9.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r2_8) || value[__cope_m3_record_type_r2_8] !== __cope_m3_record_type_r2_8 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660030_12) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660031_13) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660032_14) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660033_15) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660034_16) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660035_17) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660036_18) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660037_19)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_type_20 = Object.freeze(Object.create(null));
const __cope_m3_instances_21 = new WeakSet();

const __cope_m3_type_23 = Object.freeze(Object.create(null));
const __cope_m3_instances_24 = new WeakSet();

function __cope_m3_validate_22(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_21.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_20 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
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

function __cope_m3_validate_25(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_24.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_23 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Idle":
            if (value.$payload.length !== 0) {
                __cope_m3_panic_0();
            }
            return;
        case "Text":
            if (value.$payload.length !== 1) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "string")) {
                __cope_m3_panic_0();
            }
            return;
        case "DetailValue":
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

const __cope_m3_result_type_26 = Object.freeze(Object.create(null));

function __cope_m3_result_validate_27(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_result_type_26 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "ok":
            if (!(typeof value.$payload[0] === "string")) { __cope_m3_panic_0(); }
            return;
        case "err":
            if (!((__cope_m3_validate_22(value.$payload[0]), true))) { __cope_m3_panic_0(); }
            return;
        default:
            __cope_m3_panic_0();
    }
}

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
        if (!writer.static("$record.Detail({\n")) return false;
        if (!writer.indent(indentation + 1)) return false;
        if (!writer.static("\"label\": ")) return false;
        if (!writeString(writer, value[__cope_m3_record_field___cope_00720031002e00660030_7], indentation + 1)) return false;
        if (!writer.static(",\n")) return false;
        if (!writer.indent(indentation)) return false;
        return writer.static("})");
    }
    function writeP0R1(writer, value, indentation) {
        __cope_m3_record_require_r2_11(value);
        if (!writer.static("$record.Packet({\n")) return false;
        if (!writer.indent(indentation + 1)) return false;
        if (!writer.static("\"emptyNumbers\": ")) return false;
        if (!writeP0A0(writer, value[__cope_m3_record_field___cope_00720032002e00660030_12], indentation + 1)) return false;
        if (!writer.static(",\n")) return false;
        if (!writer.indent(indentation + 1)) return false;
        if (!writer.static("\"booleans\": ")) return false;
        if (!writeP0A1(writer, value[__cope_m3_record_field___cope_00720032002e00660031_13], indentation + 1)) return false;
        if (!writer.static(",\n")) return false;
        if (!writer.indent(indentation + 1)) return false;
        if (!writer.static("\"numbers\": ")) return false;
        if (!writeP0A0(writer, value[__cope_m3_record_field___cope_00720032002e00660032_14], indentation + 1)) return false;
        if (!writer.static(",\n")) return false;
        if (!writer.indent(indentation + 1)) return false;
        if (!writer.static("\"texts\": ")) return false;
        if (!writeP0A2(writer, value[__cope_m3_record_field___cope_00720032002e00660033_15], indentation + 1)) return false;
        if (!writer.static(",\n")) return false;
        if (!writer.indent(indentation + 1)) return false;
        if (!writer.static("\"nested\": ")) return false;
        if (!writeP0A3(writer, value[__cope_m3_record_field___cope_00720032002e00660034_16], indentation + 1)) return false;
        if (!writer.static(",\n")) return false;
        if (!writer.indent(indentation + 1)) return false;
        if (!writer.static("\"details\": ")) return false;
        if (!writeP0A4(writer, value[__cope_m3_record_field___cope_00720032002e00660035_17], indentation + 1)) return false;
        if (!writer.static(",\n")) return false;
        if (!writer.indent(indentation + 1)) return false;
        if (!writer.static("\"signals\": ")) return false;
        if (!writeP0A5(writer, value[__cope_m3_record_field___cope_00720032002e00660036_18], indentation + 1)) return false;
        if (!writer.static(",\n")) return false;
        if (!writer.indent(indentation + 1)) return false;
        if (!writer.static("\"emptyDetails\": ")) return false;
        if (!writeP0A4(writer, value[__cope_m3_record_field___cope_00720032002e00660037_19], indentation + 1)) return false;
        if (!writer.static(",\n")) return false;
        if (!writer.indent(indentation)) return false;
        return writer.static("})");
    }
    function writeP0E0(writer, value, indentation) {
        __cope_m3_validate_25(value);
        switch (value.$tag) {
            case "Idle":
                return writer.static("Signal.Idle");
            case "Text":
                if (!writer.static("Signal.Text(\n")) return false;
                if (!writer.indent(indentation + 1)) return false;
                if (!writeString(writer, value.$payload[0], indentation + 1)) return false;
                if (!writer.static("\n")) return false;
                if (!writer.indent(indentation)) return false;
                return writer.static(")");
            case "DetailValue":
                if (!writer.static("Signal.DetailValue(\n")) return false;
                if (!writer.indent(indentation + 1)) return false;
                if (!writeP0R0(writer, value.$payload[0], indentation + 1)) return false;
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
            if (typeof element !== "number") { __cope_m3_panic_0(); }
            if (!writer.indent(indentation + 1)) return false;
            if (!writeNumber(writer, element, indentation + 1)) return false;
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
            if (typeof element !== "boolean") { __cope_m3_panic_0(); }
            if (!writer.indent(indentation + 1)) return false;
            if (!writeBoolean(writer, element, indentation + 1)) return false;
            if (!writer.static(",\n")) return false;
        }
        if (!writer.indent(indentation)) return false;
        return writer.static("]");
    }
    function writeP0A2(writer, value, indentation) {
        const array = value;
        if (!Array.isArray(array)) { __cope_m3_panic_0(); }
        const length = array.length;
        if (length > 100000) return writer.outputLimit();
        if (length === 0) return writer.static("[]");
        if (!writer.static("[\n")) return false;
        for (let index = 0; index < length; index += 1) {
            if (!Object.prototype.hasOwnProperty.call(array, index)) { __cope_m3_panic_0(); }
            const element = array[index];
            if (typeof element !== "string") { __cope_m3_panic_0(); }
            if (!writer.indent(indentation + 1)) return false;
            if (!writeString(writer, element, indentation + 1)) return false;
            if (!writer.static(",\n")) return false;
        }
        if (!writer.indent(indentation)) return false;
        return writer.static("]");
    }
    function writeP0A3(writer, value, indentation) {
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
            if (!writeP0A0(writer, element, indentation + 1)) return false;
            if (!writer.static(",\n")) return false;
        }
        if (!writer.indent(indentation)) return false;
        return writer.static("]");
    }
    function writeP0A4(writer, value, indentation) {
        const array = value;
        if (!Array.isArray(array)) { __cope_m3_panic_0(); }
        const length = array.length;
        if (length > 100000) return writer.outputLimit();
        if (length === 0) return writer.static("[]");
        if (!writer.static("[\n")) return false;
        for (let index = 0; index < length; index += 1) {
            if (!Object.prototype.hasOwnProperty.call(array, index)) { __cope_m3_panic_0(); }
            const element = array[index];
            if (!writer.indent(indentation + 1)) return false;
            if (!writeP0R0(writer, element, indentation + 1)) return false;
            if (!writer.static(",\n")) return false;
        }
        if (!writer.indent(indentation)) return false;
        return writer.static("]");
    }
    function writeP0A5(writer, value, indentation) {
        const array = value;
        if (!Array.isArray(array)) { __cope_m3_panic_0(); }
        const length = array.length;
        if (length > 100000) return writer.outputLimit();
        if (length === 0) return writer.static("[]");
        if (!writer.static("[\n")) return false;
        for (let index = 0; index < length; index += 1) {
            if (!Object.prototype.hasOwnProperty.call(array, index)) { __cope_m3_panic_0(); }
            const element = array[index];
            if (!writer.indent(indentation + 1)) return false;
            if (!writeP0E0(writer, element, indentation + 1)) return false;
            if (!writer.static(",\n")) return false;
        }
        if (!writer.indent(indentation)) return false;
        return writer.static("]");
    }
    function encode0(value) {
        const writer = makeWriter(1048576, 262144);
        if (!writer.static("const $schema: string = \"copeland://corpus/runtime-array-encoding\";\n\nrecord Detail {\n    label: string;\n}\n\nrecord Packet {\n    emptyNumbers: number[];\n    booleans: boolean[];\n    numbers: number[];\n    texts: string[];\n    nested: number[][];\n    details: Detail[];\n    signals: Signal[];\n    emptyDetails: Detail[];\n}\n\nenum Signal {\n    Idle,\n    Text(value: string),\n    DetailValue(detail: Detail),\n}\n\nconst $value = ")
            || !writeP0R1(writer, value, 0)
            || !writer.static(";\n")) {
            const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
            const error = __cope_m3_make_1(__cope_m3_type_20, tag, []);
            return __cope_m3_make_1(__cope_m3_result_type_26, "err", [error]);
        }
        return __cope_m3_make_1(__cope_m3_result_type_26, "ok", [writer.finish()]);
    }

    const api = Object.create(null);
    Object.defineProperty(api, "tson0", { value: encode0, writable: false, enumerable: false, configurable: false });
    return Object.freeze(api);
})();

function encode() {
    const __cope_m3_record_init_28 = [];
    const __cope_m3_record_init_29 = [true, false, true];
    const __cope_m3_record_init_30 = [0, -0, 1.5, NaN, Infinity, -Infinity];
    const __cope_m3_record_init_31 = ["quote: \"; slash: \\; line: \n", "snow 雪 \ud83d\ude00"];
    const __cope_m3_record_init_32 = [[0, -0], [1.5], []];
    const __cope_m3_record_init_33 = "first";
    const __cope_m3_ordered_35 = __cope_m3_record_make_r1_5(__cope_m3_record_init_33);
    const __cope_m3_record_init_34 = "second";
    const __cope_m3_record_init_36 = [__cope_m3_ordered_35, __cope_m3_record_make_r1_5(__cope_m3_record_init_34)];
    const __cope_m3_ordered_38 = __cope_m3_make_1(__cope_m3_type_23, "Idle", []);
    const __cope_m3_ordered_39 = __cope_m3_make_1(__cope_m3_type_23, "Text", ["payload"]);
    const __cope_m3_record_init_37 = "nested record";
    const __cope_m3_record_init_40 = [__cope_m3_ordered_38, __cope_m3_ordered_39, __cope_m3_make_1(__cope_m3_type_23, "DetailValue", [__cope_m3_record_make_r1_5(__cope_m3_record_init_37)])];
    const __cope_m3_record_init_41 = [];
    const loaded = __cope_m3_record_make_r2_10(__cope_m3_record_init_28, __cope_m3_record_init_29, __cope_m3_record_init_30, __cope_m3_record_init_31, __cope_m3_record_init_32, __cope_m3_record_init_36, __cope_m3_record_init_40, __cope_m3_record_init_41);
    return __cope_m3_tson_2["tson0"](loaded);
}
