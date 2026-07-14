"use strict";
function $终甲() {
  throw new Error("Copeland JavaScript backend invariant failure.");
}
function $值造甲(type, tag, payload) {
  const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
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
const $果型甲 = Object.freeze(Object.create(null));
function $果验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $果型甲 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
    $终甲();
  }
  switch (value.$tag) {
    case "ok":
      if (!(typeof value.$payload[0] === "number")) { $终甲(); }
      return;
    case "err":
      if (!(typeof value.$payload[0] === "string")) { $终甲(); }
      return;
    default:
      $终甲();
  }
}
function read() {
  return $值造甲($果型甲, "ok", [4]);
}
function main() {
  const $临甲 = (() => {
    const $临乙 = (() => {
      const $临戊 = read();
      $果验甲($临戊);
      if ($临戊.$tag === "err") {
        return $流接甲(1, $临戊.$payload[0]);
      }
      const value = $临戊.$payload[0];
      return $流值甲((value + 1));
    })();
    $流验甲($临乙);
    let $临丁;
    if ($临乙.$kind === "handler" && $临乙.$handler === 1) {
      const error = $临乙.$error;
      const $临丙 = (() => {
        return $流值甲(0);
      })();
      $流验甲($临丙);
      if ($临丙.$kind !== "value") {
        return $临丙;
      }
      $临丁 = $临丙.$value;
    } else if ($临乙.$kind === "value") {
      $临丁 = $临乙.$value;
    } else {
      return $临乙;
    }
    return $流值甲($临丁);
    return $流值甲(undefined);
  })();
  $流验甲($临甲);
  if ($临甲.$kind === "value") {
    return $临甲.$value;
  }
  if ($临甲.$kind === "function") {
    $终甲();
  }
  $终甲();
}
