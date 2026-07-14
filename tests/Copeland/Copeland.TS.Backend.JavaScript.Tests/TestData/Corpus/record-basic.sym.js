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
const $录型乙 = Symbol("$录型乙");
const $录印乙 = new WeakSet();
const $录域丙 = Symbol("$录域丙");
function $录造乙(field0) {
  const value = Object.create(null);
  Object.defineProperties(value, {
    [$录型乙]: { value: $录型乙, writable: false, enumerable: false, configurable: false },
    [$录域丙]: { value: field0, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze(value);
  $录印乙.add(value);
  return value;
}
function $录验乙(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !$录印乙.has(value) || !Object.prototype.hasOwnProperty.call(value, $录型乙) || value[$录型乙] !== $录型乙 || !Object.prototype.hasOwnProperty.call(value, $录域丙)) {
    $终甲();
  }
}
function main() {
  const $临甲 = 40;
  const $临乙 = 2;
  const $临丙 = $录造甲($临甲, $临乙);
  const envelope = $录造乙($临丙);
  const $临丁 = envelope;
  $录验乙($临丁);
  const $临戊 = $临丁[$录域丙];
  $录验甲($临戊);
  const $临辛 = $临戊[$录域甲];
  const $临己 = envelope;
  $录验乙($临己);
  const $临庚 = $临己[$录域丙];
  $录验甲($临庚);
  return ($临辛 + $临庚[$录域乙]);
}
