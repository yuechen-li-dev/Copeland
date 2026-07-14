"use strict";
function $终甲() {
  throw new Error("Copeland JavaScript backend invariant failure.");
}
function $值造甲(type, tag, payload) {
  const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
  return value;
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
function good() {
  return $值造甲($果型甲, "ok", [4]);
}
function stored() {
  const outcome = good();
  const $临甲 = outcome;
  $果验甲($临甲);
  if ($临甲.$tag === "err") {
    return $值造甲($果型甲, "err", [$临甲.$payload[0]]);
  }
  const value = $临甲.$payload[0];
  return $值造甲($果型甲, "ok", [(value + 1)]);
}
