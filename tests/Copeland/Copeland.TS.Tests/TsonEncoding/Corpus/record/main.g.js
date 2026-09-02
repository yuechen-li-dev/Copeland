"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_make_1(type, tag, payload) {
    const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
    if (type === __cope_m3_type_15) __cope_m3_instances_16.add(value);
    if (type === __cope_m3_type_18) __cope_m3_instances_19.add(value);
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

function __cope_m3_record_make_r2_10(field0, field1, field2) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r2_8]: { value: __cope_m3_record_type_r2_8, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660030_12]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660031_13]: { value: field1, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660032_14]: { value: field2, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r2_9.add(value);
    return value;
}

function __cope_m3_record_require_r2_11(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r2_9.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r2_8) || value[__cope_m3_record_type_r2_8] !== __cope_m3_record_type_r2_8 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660030_12) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660031_13) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660032_14)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_type_15 = Object.freeze(Object.create(null));
const __cope_m3_instances_16 = new WeakSet();

const __cope_m3_type_18 = Object.freeze(Object.create(null));
const __cope_m3_instances_19 = new WeakSet();

function __cope_m3_validate_17(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_16.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_15 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
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

function __cope_m3_validate_20(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_19.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_18 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
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
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !((__cope_m3_record_require_r1_6(value.$payload[0]), true))) {
                __cope_m3_panic_0();
            }
            return;
        default:
            __cope_m3_panic_0();
    }
}

const __cope_m3_result_type_21 = Object.freeze(Object.create(null));

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
        if (!writer.static("$record.Settings({\n")) return false;
        if (!writer.indent(indentation + 1)) return false;
        if (!writer.static("\"enabled\": ")) return false;
        if (!writeBoolean(writer, value[__cope_m3_record_field___cope_00720032002e00660030_12], indentation + 1)) return false;
        if (!writer.static(",\n")) return false;
        if (!writer.indent(indentation + 1)) return false;
        if (!writer.static("\"count\": ")) return false;
        if (!writeNumber(writer, value[__cope_m3_record_field___cope_00720032002e00660031_13], indentation + 1)) return false;
        if (!writer.static(",\n")) return false;
        if (!writer.indent(indentation + 1)) return false;
        if (!writer.static("\"mode\": ")) return false;
        if (!writeP0E0(writer, value[__cope_m3_record_field___cope_00720032002e00660032_14], indentation + 1)) return false;
        if (!writer.static(",\n")) return false;
        if (!writer.indent(indentation)) return false;
        return writer.static("})");
    }
    function writeP0E0(writer, value, indentation) {
        __cope_m3_validate_20(value);
        switch (value.$tag) {
            case "Off":
                return writer.static("Mode.Off");
            case "Named":
                if (!writer.static("Mode.Named(\n")) return false;
                if (!writer.indent(indentation + 1)) return false;
                if (!writeP0R0(writer, value.$payload[0], indentation + 1)) return false;
                if (!writer.static("\n")) return false;
                if (!writer.indent(indentation)) return false;
                return writer.static(")");
            default:
                __cope_m3_panic_0();
        }
    }
    function encode0(value) {
        const writer = makeWriter(1048576, 262144);
        if (!writer.static("const $schema: string = \"copeland://corpus/runtime-encoding\";\n\nrecord Detail {\n    label: string;\n}\n\nenum Mode {\n    Off,\n    Named(detail: Detail),\n}\n\nrecord Settings {\n    enabled: boolean;\n    count: number;\n    mode: Mode;\n}\n\nconst $value = ")
            || !writeP0R1(writer, value, 0)
            || !writer.static(";\n")) {
            const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
            const error = __cope_m3_make_1(__cope_m3_type_15, tag, []);
            return __cope_m3_make_1(__cope_m3_result_type_21, "err", [error]);
        }
        return __cope_m3_make_1(__cope_m3_result_type_21, "ok", [writer.finish()]);
    }

    const api = Object.create(null);
    Object.defineProperty(api, "tson0", { value: encode0, writable: false, enumerable: false, configurable: false });
    return Object.freeze(api);
})();

function encode() {
    const __cope_m3_record_init_23 = true;
    const __cope_m3_record_init_24 = -0;
    const __cope_m3_record_init_25 = "snow 雪 \ud83d\ude00";
    const __cope_m3_record_init_26 = __cope_m3_make_1(__cope_m3_type_18, "Named", [__cope_m3_record_make_r1_5(__cope_m3_record_init_25)]);
    const loaded = __cope_m3_record_make_r2_10(__cope_m3_record_init_23, __cope_m3_record_init_24, __cope_m3_record_init_26);
    return __cope_m3_tson_2["tson0"](loaded);
}
