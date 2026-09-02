"use strict";
function $终甲() {
  throw new Error("Copeland JavaScript backend invariant failure.");
}
function $值造甲(type, tag, payload) {
  const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
  if (type === $枚型甲) $枚印甲.add(value);
  if (type === $枚型乙) $枚印乙.add(value);
  return value;
}
const $录型甲 = Symbol("$录型甲");
const $录印甲 = new WeakSet();
const $录域甲 = Symbol("$录域甲");
function $录造甲(field0) {
  const value = Object.create(null);
  Object.defineProperties(value, {
    [$录型甲]: { value: $录型甲, writable: false, enumerable: false, configurable: false },
    [$录域甲]: { value: field0, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze(value);
  $录印甲.add(value);
  return value;
}
function $录验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !$录印甲.has(value) || !Object.prototype.hasOwnProperty.call(value, $录型甲) || value[$录型甲] !== $录型甲 || !Object.prototype.hasOwnProperty.call(value, $录域甲)) {
    $终甲();
  }
}
const $录型乙 = Symbol("$录型乙");
const $录印乙 = new WeakSet();
const $录域乙 = Symbol("$录域乙");
const $录域丙 = Symbol("$录域丙");
const $录域丁 = Symbol("$录域丁");
function $录造乙(field0, field1, field2) {
  const value = Object.create(null);
  Object.defineProperties(value, {
    [$录型乙]: { value: $录型乙, writable: false, enumerable: false, configurable: false },
    [$录域乙]: { value: field0, writable: false, enumerable: false, configurable: false },
    [$录域丙]: { value: field1, writable: false, enumerable: false, configurable: false },
    [$录域丁]: { value: field2, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze(value);
  $录印乙.add(value);
  return value;
}
function $录验乙(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !$录印乙.has(value) || !Object.prototype.hasOwnProperty.call(value, $录型乙) || value[$录型乙] !== $录型乙 || !Object.prototype.hasOwnProperty.call(value, $录域乙) || !Object.prototype.hasOwnProperty.call(value, $录域丙) || !Object.prototype.hasOwnProperty.call(value, $录域丁)) {
    $终甲();
  }
}
const $枚型甲 = Object.freeze(Object.create(null));
const $枚印甲 = new WeakSet();
const $枚型乙 = Object.freeze(Object.create(null));
const $枚印乙 = new WeakSet();
function $枚验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !$枚印甲.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $枚型甲 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
    $终甲();
  }
  switch (value.$tag) {
    case "InvalidUnicode":
      if (value.$payload.length !== 0) {
        $终甲();
      }
      return;
    case "OutputLimitExceeded":
      if (value.$payload.length !== 0) {
        $终甲();
      }
      return;
    default:
      $终甲();
  }
}
function $枚验乙(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !$枚印乙.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $枚型乙 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
    $终甲();
  }
  switch (value.$tag) {
    case "Off":
      if (value.$payload.length !== 0) {
        $终甲();
      }
      return;
    case "Named":
      if (value.$payload.length !== 1) {
        $终甲();
      }
      if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(($录验甲(value.$payload[0]), true))) {
        $终甲();
      }
      return;
    default:
      $终甲();
  }
}
const $果型甲 = Object.freeze(Object.create(null));
const $运编甲 = (() => {
  function $写造甲(maximumBytes, maximumStringCodeUnits) {
    const parts = [];
    const bitsBuffer = new ArrayBuffer(8);
    const bitsView = new DataView(bitsBuffer);
    let byteCount = 0;
    let error = null;
    function $写错甲(kind) { if (error === null) error = kind; return false; }
    function $写附甲(value) {
      let added = 0;
      for (let index = 0; index < value.length; index += 1) {
        const code = value.charCodeAt(index);
        if (code <= 0x7F) added += 1;
        else if (code <= 0x7FF) added += 2;
        else if (code >= 0xD800 && code <= 0xDBFF) {
          if (index + 1 >= value.length) return $写错甲("invalid");
          const low = value.charCodeAt(index + 1);
          if (low < 0xDC00 || low > 0xDFFF) return $写错甲("invalid");
          added += 4;
          index += 1;
        } else if (code >= 0xDC00 && code <= 0xDFFF) return $写错甲("invalid");
        else added += 3;
      }
      if (byteCount > maximumBytes - added) return $写错甲("limit");
      byteCount += added;
      parts.push(value);
      return true;
    }
    function $串编甲(code) { return $写附甲("\\u" + code.toString(16).toUpperCase().padStart(4, "0")); }
    function $串写乙(value) {
      if (value.length > maximumStringCodeUnits) return $写错甲("limit");
      for (let index = 0; index < value.length; index += 1) {
        const code = value.charCodeAt(index);
        if (code >= 0xD800 && code <= 0xDBFF) {
          if (index + 1 >= value.length) return $写错甲("invalid");
          const low = value.charCodeAt(index + 1);
          if (low < 0xDC00 || low > 0xDFFF) return $写错甲("invalid");
          index += 1;
        } else if (code >= 0xDC00 && code <= 0xDFFF) return $写错甲("invalid");
      }
      if (!$写附甲("\"")) return false;
      for (let index = 0; index < value.length; index += 1) {
        const code = value.charCodeAt(index);
        if (code === 0x22) { if (!$写附甲("\\\"")) return false; }
        else if (code === 0x5C) { if (!$写附甲("\\\\")) return false; }
        else if (code === 0x08) { if (!$写附甲("\\b")) return false; }
        else if (code === 0x0C) { if (!$写附甲("\\f")) return false; }
        else if (code === 0x0A) { if (!$写附甲("\\n")) return false; }
        else if (code === 0x0D) { if (!$写附甲("\\r")) return false; }
        else if (code === 0x09) { if (!$写附甲("\\t")) return false; }
        else if (code < 0x20 || code === 0x2028 || code === 0x2029) { if (!$串编甲(code)) return false; }
        else if (code >= 0xD800 && code <= 0xDBFF) {
          if (!$写附甲(value.slice(index, index + 2))) return false;
          index += 1;
        } else if (!$写附甲(value[index])) return false;
      }
      return $写附甲("\"");
    }
    function $数写乙(value) {
      bitsView.setFloat64(0, value, false);
      let high = bitsView.getUint32(0, false);
      let low = bitsView.getUint32(4, false);
      if ((high & 0x7FF00000) === 0x7FF00000 && ((high & 0x000FFFFF) !== 0 || low !== 0)) { high = 0x7FF80000; low = 0; }
      const hexadecimal = high.toString(16).toUpperCase().padStart(8, "0") + low.toString(16).toUpperCase().padStart(8, "0");
      return $写附甲("$number(\"" + hexadecimal + "\")");
    }
    return Object.freeze({
      static: $写附甲,
      indent: level => $写附甲(" ".repeat(level * 4)),
      string: $串写乙,
      number: $数写乙,
      error: () => error,
      finish: () => parts.join(""),
    });
  }
  function $布写甲(writer, value, indentation) { return writer.static(value ? "true" : "false"); }
  function $数写甲(writer, value, indentation) { return writer.number(value); }
  function $串写甲(writer, value, indentation) { return writer.string(value); }
  function $录写甲(writer, value, indentation) {
    $录验甲(value);
    if (!writer.static("$record.Detail({\n")) return false;
    if (!writer.indent(indentation + 1)) return false;
    if (!writer.static("\"label\": ")) return false;
    if (!$串写甲(writer, value[$录域甲], indentation + 1)) return false;
    if (!writer.static(",\n")) return false;
    if (!writer.indent(indentation)) return false;
    return writer.static("})");
  }
  function $录写乙(writer, value, indentation) {
    $录验乙(value);
    if (!writer.static("$record.Settings({\n")) return false;
    if (!writer.indent(indentation + 1)) return false;
    if (!writer.static("\"enabled\": ")) return false;
    if (!$布写甲(writer, value[$录域乙], indentation + 1)) return false;
    if (!writer.static(",\n")) return false;
    if (!writer.indent(indentation + 1)) return false;
    if (!writer.static("\"count\": ")) return false;
    if (!$数写甲(writer, value[$录域丙], indentation + 1)) return false;
    if (!writer.static(",\n")) return false;
    if (!writer.indent(indentation + 1)) return false;
    if (!writer.static("\"mode\": ")) return false;
    if (!$枚写甲(writer, value[$录域丁], indentation + 1)) return false;
    if (!writer.static(",\n")) return false;
    if (!writer.indent(indentation)) return false;
    return writer.static("})");
  }
  function $枚写甲(writer, value, indentation) {
    $枚验乙(value);
    switch (value.$tag) {
      case "Off":
        return writer.static("Mode.Off");
      case "Named":
        if (!writer.static("Mode.Named(\n")) return false;
        if (!writer.indent(indentation + 1)) return false;
        if (!$录写甲(writer, value.$payload[0], indentation + 1)) return false;
        if (!writer.static("\n")) return false;
        if (!writer.indent(indentation)) return false;
        return writer.static(")");
      default:
        $终甲();
    }
  }
  function $编甲(value) {
    const writer = $写造甲(1048576, 262144);
    if (!writer.static("const $schema: string = \"copeland://corpus/runtime-encoding\";\n\nrecord Detail {\n    label: string;\n}\n\nenum Mode {\n    Off,\n    Named(detail: Detail),\n}\n\nrecord Settings {\n    enabled: boolean;\n    count: number;\n    mode: Mode;\n}\n\nconst $value = ")
      || !$录写乙(writer, value, 0)
      || !writer.static(";\n")) {
      const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
      const error = $值造甲($枚型甲, tag, []);
      return $值造甲($果型甲, "err", [error]);
    }
    return $值造甲($果型甲, "ok", [writer.finish()]);
  }
  const api = Object.create(null);
  Object.defineProperty(api, "tson0", { value: $编甲, writable: false, enumerable: false, configurable: false });
  return Object.freeze(api);
})();
function encode() {
  const $临甲 = true;
  const $临乙 = -0;
  const $临丙 = "snow 雪 \ud83d\ude00";
  const $临丁 = $值造甲($枚型乙, "Named", [$录造甲($临丙)]);
  const loaded = $录造乙($临甲, $临乙, $临丁);
  return $运编甲["tson0"](loaded);
}
