"use strict";
function $终甲() {
  throw new Error("Copeland JavaScript backend invariant failure.");
}
const $录型甲 = Symbol("$录型甲");
const $录印甲 = new WeakSet();
const $录域甲 = Symbol("$录域甲");
const $录域乙 = Symbol("$录域乙");
function $录造甲(field0, field1) {
  const value = Object.create(null);
  Object.defineProperties(value, {
    [$录型甲]: { value: $录型甲, writable: false, enumerable: false, configurable: false },
    [$录域甲]: { value: field0, writable: false, enumerable: false, configurable: false },
    [$录域乙]: { value: field1, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze(value);
  $录印甲.add(value);
  return value;
}
function $录验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !$录印甲.has(value) || !Object.prototype.hasOwnProperty.call(value, $录型甲) || value[$录型甲] !== $录型甲 || !Object.prototype.hasOwnProperty.call(value, $录域甲) || !Object.prototype.hasOwnProperty.call(value, $录域乙)) {
    $终甲();
  }
}
function identity__primitive_number__8F3BAE8FF0D9F338(value) {
  return value;
}
function sum__record_Point__AD972DE51050F4B6(value) {
  const $临甲 = value;
  $录验甲($临甲);
  const $临丙 = $临甲[$录域甲];
  const $临乙 = value;
  $录验甲($临乙);
  return ($临丙 + $临乙[$录域乙]);
}
function main() {
  const $临丁 = 20;
  const $临戊 = 22;
  const point = $录造甲($临丁, $临戊);
  const explicit = identity__primitive_number__8F3BAE8FF0D9F338(sum__record_Point__AD972DE51050F4B6(point));
  const inferred = identity__primitive_number__8F3BAE8FF0D9F338(sum__record_Point__AD972DE51050F4B6(point));
  return (explicit + inferred);
}
