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
const $枚型甲 = Object.freeze(Object.create(null));
const $枚印甲 = new WeakSet();
const $枚型乙 = Object.freeze(Object.create(null));
const $枚印乙 = new WeakSet();
function $枚验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !$枚印甲.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $枚型甲 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
    $终甲();
  }
  switch (value.$tag) {
    case "None":
      if (value.$payload.length !== 0) {
        $终甲();
      }
      return;
    case "Number":
      if (value.$payload.length !== 1) {
        $终甲();
      }
      if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "number")) {
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
    case "Empty":
      if (value.$payload.length !== 0) {
        $终甲();
      }
      return;
    case "Single":
      if (value.$payload.length !== 1) {
        $终甲();
      }
      if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "number")) {
        $终甲();
      }
      return;
    case "Pair":
      if (value.$payload.length !== 2) {
        $终甲();
      }
      if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "number")) {
        $终甲();
      }
      if (!Object.prototype.hasOwnProperty.call(value.$payload, 1) || !(typeof value.$payload[1] === "string")) {
        $终甲();
      }
      return;
    case "Nested":
      if (value.$payload.length !== 1) {
        $终甲();
      }
      if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(($枚验甲(value.$payload[0]), true))) {
        $终甲();
      }
      return;
    default:
      $终甲();
  }
}
function main() {
  const outer = $值造甲($枚型乙, "Nested", [$值造甲($枚型甲, "Number", [9])]);
  return (() => { const $配临甲 = outer; $枚验乙($配临甲); switch ($配临甲.$tag) { case "Empty": { return "empty"; } case "Single": { const value = $配临甲.$payload[0]; return "single"; } case "Pair": { const first = $配临甲.$payload[0]; const second = $配临甲.$payload[1]; return second; } case "Nested": { const inner = $配临甲.$payload[0]; return (() => { const $配临乙 = inner; $枚验甲($配临乙); switch ($配临乙.$tag) { case "None": { return "none"; } case "Number": { const value = $配临乙.$payload[0]; return "nested"; } default: return $终甲(); } })(); } default: return $终甲(); } })();
}
