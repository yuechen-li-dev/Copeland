"use strict";
const __cope_callable_instances = new WeakSet();
const __cope_callable_signatures = new WeakMap();
const __cope_callable_codes = new WeakMap();
const __cope_callable_host_carriers = new WeakMap();
function __cope_callable_ref(signature, code) {
  const carrier = Object.create(null);
  __cope_callable_signatures.set(carrier, signature);
  __cope_callable_codes.set(carrier, code);
  __cope_callable_instances.add(carrier);
  return Object.freeze(carrier);
}
function __cope_callable_host(signature, hostCallable) {
  if (typeof hostCallable !== "function") throw new Error("COPE-PANIC-CALLABLE: host returned a non-callable value");
  return __cope_callable_ref(signature, (...argumentsInOrder) => hostCallable(...argumentsInOrder));
}
function __cope_callable_host_retained(signature, hostCallable) {
  if (typeof hostCallable !== "function") throw new Error("COPE-PANIC-CALLABLE: host supplied a non-callable callback argument");
  let bySignature = __cope_callable_host_carriers.get(hostCallable);
  if (bySignature === undefined) {
    bySignature = new Map();
    __cope_callable_host_carriers.set(hostCallable, bySignature);
  }
  let carrier = bySignature.get(signature);
  if (carrier === undefined) {
    carrier = __cope_callable_host(signature, hostCallable);
    bySignature.set(signature, carrier);
  }
  return carrier;
}
function __cope_callable_invoke(carrier, signature, argumentsInOrder) {
  if (!__cope_callable_instances.has(carrier) || __cope_callable_signatures.get(carrier) !== signature) throw new Error("COPE-PANIC-CALLABLE: invalid callable");
  const code = __cope_callable_codes.get(carrier);
  return code(...argumentsInOrder);
}
function $终甲() {
  throw new Error("Copeland JavaScript backend invariant failure.");
}
function $值造甲(type, tag, payload) {
  const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
  if (type === $枚型甲) $枚印甲.add(value);
  return value;
}
const $流符甲 = Object.freeze(Object.create(null));
function $流值甲(value) {
  return Object.freeze(Object.assign(Object.create(null), { $flow: $流符甲, $kind: "value", $value: value }));
}
function $流接甲(handler, error) {
  return Object.freeze(Object.assign(Object.create(null), { $flow: $流符甲, $kind: "handler", $handler: handler, $error: error }));
}
function $流函甲(error) {
  return Object.freeze(Object.assign(Object.create(null), { $flow: $流符甲, $kind: "function", $error: error }));
}
function $流验甲(flow) {
  if (typeof flow !== "object" || flow === null || Object.getPrototypeOf(flow) !== null || !Object.isFrozen(flow) || flow.$flow !== $流符甲 || typeof flow.$kind !== "string") {
    $终甲();
  }
  switch (flow.$kind) {
    case "value":
      if (!Object.prototype.hasOwnProperty.call(flow, "$value")) { $终甲(); }
      return;
    case "handler":
      if (!Number.isInteger(flow.$handler) || !Object.prototype.hasOwnProperty.call(flow, "$error")) { $终甲(); }
      return;
    case "function":
      if (!Object.prototype.hasOwnProperty.call(flow, "$error")) { $终甲(); }
      return;
    default:
      $终甲();
  }
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
function $枚验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !$枚印甲.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $枚型甲 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
    $终甲();
  }
  switch (value.$tag) {
    case "InvalidAge":
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
const $果型甲 = Object.freeze(Object.create(null));
function $果验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $果型甲 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
    $终甲();
  }
  switch (value.$tag) {
    case "ok":
      if (!(($录验乙(value.$payload[0]), true))) { $终甲(); }
      return;
    case "err":
      if (!(($枚验甲(value.$payload[0]), true))) { $终甲(); }
      return;
    default:
      $终甲();
  }
}
function Person__constructor(name, age) {
  if ((age < 0)) {
    return $值造甲($果型甲, "err", [$值造甲($枚型甲, "InvalidAge", [age])]);
  }
  const $临甲 = name;
  const $临乙 = Person__normalize(name);
  const $临丙 = age;
  return $值造甲($果型甲, "ok", [$录造乙($临甲, $临乙, $临丙)]);
}
function Person__normalize(name) {
  return name;
}
function Person__birthday(person) {
  const $临丁 = person;
  $录验乙($临丁);
  const $临戊 = person;
  $录验乙($临戊);
  const $临己 = ($临戊[$录域丁] + 1);
  return $录造乙($临丁[$录域乙], $临丁[$录域丙], $临己);
}
function Person__identity__primitive_number__7F14E33913752003(value) {
  return value;
}
function label__record_Person__66BA86D43998F2CA(value) {
  const $临庚 = value;
  $录验乙($临庚);
  return $临庚[$录域乙];
}
function main() {
  const $临辛 = (() => {
    const $临壬 = (() => {
      const $临甲乙 = Person__constructor("Ada", 41);
      $果验甲($临甲乙);
      if ($临甲乙.$tag === "err") {
        return $流接甲(1, $临甲乙.$payload[0]);
      }
      const person = $临甲乙.$payload[0];
      const $临甲丙 = [person];
      const envelope = $录造甲($临甲丙);
      const birthday = __cope_callable_ref("(record:r2)->record:r2", Person__birthday);
      const older = __cope_callable_invoke(birthday, "(record:r2)->record:r2", [person]);
      const $临甲丁 = older;
      $录验乙($临甲丁);
      const age = Person__identity__primitive_number__7F14E33913752003($临甲丁[$录域丁]);
      return $流值甲(label__record_Person__66BA86D43998F2CA(older));
    })();
    $流验甲($临壬);
    let $临甲甲;
    if ($临壬.$kind === "handler" && $临壬.$handler === 1) {
      const error = $临壬.$error;
      const $临癸 = (() => {
        return $流值甲("recovered");
      })();
      $流验甲($临癸);
      if ($临癸.$kind !== "value") {
        return $临癸;
      }
      $临甲甲 = $临癸.$value;
    } else if ($临壬.$kind === "value") {
      $临甲甲 = $临壬.$value;
    } else {
      return $临壬;
    }
    return $流值甲($临甲甲);
    return $流值甲(undefined);
  })();
  $流验甲($临辛);
  if ($临辛.$kind === "value") {
    return $临辛.$value;
  }
  if ($临辛.$kind === "function") {
    $终甲();
  }
  $终甲();
}
