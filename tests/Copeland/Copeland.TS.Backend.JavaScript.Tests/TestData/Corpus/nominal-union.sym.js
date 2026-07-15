"use strict";
function $终甲() {
  throw new Error("Copeland JavaScript backend invariant failure.");
}
function $值造甲(type, tag, payload) {
  const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
  if (type === $枚型甲) $枚印甲.add(value);
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
function $录造乙(field0, field1) {
  const value = Object.create(null);
  Object.defineProperties(value, {
    [$录型乙]: { value: $录型乙, writable: false, enumerable: false, configurable: false },
    [$录域乙]: { value: field0, writable: false, enumerable: false, configurable: false },
    [$录域丙]: { value: field1, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze(value);
  $录印乙.add(value);
  return value;
}
function $录验乙(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !$录印乙.has(value) || !Object.prototype.hasOwnProperty.call(value, $录型乙) || value[$录型乙] !== $录型乙 || !Object.prototype.hasOwnProperty.call(value, $录域乙) || !Object.prototype.hasOwnProperty.call(value, $录域丙)) {
    $终甲();
  }
}
const $枚型甲 = Object.freeze(Object.create(null));
const $枚印甲 = new WeakSet();
function $枚验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !$枚印甲.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $枚型甲 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
    $终甲();
  }
  switch (value.$tag) {
    case "Circle":
      if (value.$payload.length !== 1) {
        $终甲();
      }
      if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(($录验甲(value.$payload[0]), true))) {
        $终甲();
      }
      return;
    case "Rectangle":
      if (value.$payload.length !== 1) {
        $终甲();
      }
      if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(($录验乙(value.$payload[0]), true))) {
        $终甲();
      }
      return;
    default:
      $终甲();
  }
}
function main() {
  const $临甲 = 4;
  const circle = $录造甲($临甲);
  const shape = $值造甲($枚型甲, "Circle", [circle]);
  const $临乙 = shape;
  $枚验甲($临乙);
  let $临丙;
  switch ($临乙.$tag) {
    case "Circle":
    {
      const value = $临乙.$payload[0];
      const $临丁 = value;
      $录验甲($临丁);
      const $临己 = $临丁[$录域甲];
      const $临戊 = value;
      $录验甲($临戊);
      $临丙 = ($临己 * $临戊[$录域甲]);
      break;
    }
    case "Rectangle":
    {
      const value = $临乙.$payload[0];
      const $临庚 = value;
      $录验乙($临庚);
      const $临壬 = $临庚[$录域乙];
      const $临辛 = value;
      $录验乙($临辛);
      $临丙 = ($临壬 * $临辛[$录域丙]);
      break;
    }
    default:
      $终甲();
  }
  return $临丙;
}
