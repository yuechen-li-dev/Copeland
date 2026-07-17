"use strict";
const __cope_callable_instances = new WeakSet();
const __cope_callable_signatures = new WeakMap();
const __cope_callable_codes = new WeakMap();
const __cope_callable_environments = new WeakMap();
const __cope_callable_environment_instances = new WeakSet();
const __cope_callable_environment_values = new WeakMap();
function __cope_callable_ref(signature, code) {
  const carrier = Object.create(null);
  __cope_callable_signatures.set(carrier, signature);
  __cope_callable_codes.set(carrier, code);
  __cope_callable_instances.add(carrier);
  return Object.freeze(carrier);
}
function __cope_callable_capture(signature, code, values) {
  const environment = Object.create(null);
  __cope_callable_environment_values.set(environment, Object.freeze(values.slice()));
  __cope_callable_environment_instances.add(environment);
  Object.freeze(environment);
  const carrier = Object.create(null);
  __cope_callable_signatures.set(carrier, signature);
  __cope_callable_codes.set(carrier, code);
  __cope_callable_environments.set(carrier, environment);
  __cope_callable_instances.add(carrier);
  return Object.freeze(carrier);
}
function __cope_callable_invoke(carrier, signature, argumentsInOrder) {
  if (!__cope_callable_instances.has(carrier) || __cope_callable_signatures.get(carrier) !== signature) throw new Error("COPE-PANIC-CALLABLE: invalid callable");
  const code = __cope_callable_codes.get(carrier);
  const environment = __cope_callable_environments.get(carrier);
  if (environment === undefined) return code(...argumentsInOrder);
  if (!__cope_callable_environment_instances.has(environment)) throw new Error("COPE-PANIC-CALLABLE: invalid environment");
  return code(...__cope_callable_environment_values.get(environment), ...argumentsInOrder);
}
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
const $枚型甲 = Object.freeze(Object.create(null));
const $枚印甲 = new WeakSet();
const $枚型乙 = Object.freeze(Object.create(null));
const $枚印乙 = new WeakSet();
function $枚验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !$枚印甲.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $枚型甲 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
    $终甲();
  }
  switch (value.$tag) {
    case "Value":
      if (value.$payload.length !== 1) {
        $终甲();
      }
      if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(__cope_callable_instances.has(value.$payload[0]) && __cope_callable_signatures.get(value.$payload[0]) === "(named:number)->named:number")) {
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
    case "Bad":
      if (value.$payload.length !== 0) {
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
      if (!(__cope_callable_instances.has(value.$payload[0]) && __cope_callable_signatures.get(value.$payload[0]) === "(named:number)->named:number")) { $终甲(); }
      return;
    case "err":
      if (!(($枚验乙(value.$payload[0]), true))) { $终甲(); }
      return;
    default:
      $终甲();
  }
}
function increment(value) {
  return (value + 1);
}
function apply(operation, value) {
  return __cope_callable_invoke(operation, "(named:number)->named:number", [value]);
}
function __cope_arrow_0(base, value) {
  return (base + value);
}
function makeAdder(base) {
  return __cope_callable_capture("(named:number)->named:number", __cope_arrow_0, [base]);
}
function identity__primitive_number__8F3BAE8FF0D9F338(value) {
  return value;
}
function __cope_arrow_1(value) {
  return (value * 2);
}
function __cope_arrow_2(value) {
  const adjusted = (value + 1);
  return (adjusted * 2);
}
function main() {
  const named = __cope_callable_ref("(named:number)->named:number", increment);
  const closed = __cope_callable_ref("(named:number)->named:number", identity__primitive_number__8F3BAE8FF0D9F338);
  const double = __cope_callable_ref("(named:number)->named:number", __cope_arrow_1);
  const block = __cope_callable_ref("(named:number)->named:number", __cope_arrow_2);
  const escaped = makeAdder(10);
  const stored = [named, closed, double, block, escaped];
  const $临甲 = escaped;
  const box = $录造甲($临甲);
  const $临乙 = box;
  $录验甲($临乙);
  const choice = $值造甲($枚型甲, "Value", [$临乙[$录域甲]]);
  const result = $值造甲($果型甲, "ok", [(() => { const $配临甲 = choice; $枚验甲($配临甲); switch ($配临甲.$tag) { case "Value": { const operation = $配临甲.$payload[0]; return operation; } default: return $终甲(); } })()]);
  const $临丙 = result;
  $果验甲($临丙);
  let $临丁;
  switch ($临丙.$tag) {
    case "ok": {
      const operation = $临丙.$payload[0];
      $临丁 = apply(operation, 1);
      break;
    }
    case "err": {
      const error = $临丙.$payload[0];
      $临丁 = 0;
      break;
    }
    default:
      $终甲();
  }
  return $临丁;
}
