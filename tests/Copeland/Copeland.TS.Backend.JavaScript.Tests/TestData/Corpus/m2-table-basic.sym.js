"use strict";
function $终甲() {
  throw new Error("Copeland JavaScript backend invariant failure.");
}
function $终解甲(error) {
  const panic = new Error("COPE-PANIC-UNWRAP: Result unwrap encountered err");
  panic.error = error;
  throw panic;
}
function $值造甲(type, tag, payload) {
  const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
  if (type === $枚型甲) $枚印甲.add(value);
  return value;
}
const $枚型甲 = Object.freeze(Object.create(null));
const $枚印甲 = new WeakSet();
function $枚验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !$枚印甲.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $枚型甲 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
    $终甲();
  }
  switch (value.$tag) {
    case "InvalidIndex":
      if (value.$payload.length !== 1) {
        $终甲();
      }
      if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "number")) {
        $终甲();
      }
      return;
    case "OutOfBounds":
      if (value.$payload.length !== 2) {
        $终甲();
      }
      if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "number")) {
        $终甲();
      }
      if (!Object.prototype.hasOwnProperty.call(value.$payload, 1) || !(typeof value.$payload[1] === "number")) {
        $终甲();
      }
      return;
    default:
      $终甲();
  }
}
const $果型甲 = Object.freeze(Object.create(null));
const $果型乙 = Object.freeze(Object.create(null));
const $果型丙 = Object.freeze(Object.create(null));
const $果型丁 = Object.freeze(Object.create(null));
function $果验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $果型甲 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
    $终甲();
  }
  switch (value.$tag) {
    case "ok":
      if (!(typeof value.$payload[0] === "number")) { $终甲(); }
      return;
    case "err":
      if (!(($枚验甲(value.$payload[0]), true))) { $终甲(); }
      return;
    default:
      $终甲();
  }
}
function $果验丁(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $果型丁 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
    $终甲();
  }
  switch (value.$tag) {
    case "ok":
      if (!(($表行验乙(value.$payload[0]), true))) { $终甲(); }
      return;
    case "err":
      if (!(($枚验甲(value.$payload[0]), true))) { $终甲(); }
      return;
    default:
      $终甲();
  }
}
const $列型甲 = Symbol("$列型甲");
const $列取甲 = Symbol("$列取甲");
const $表行表甲 = Symbol("$表行表甲");
const $表行序甲 = Symbol("$表行序甲");
function $列验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, $列型甲) || value[$列型甲] !== $列型甲 || !Object.prototype.hasOwnProperty.call(value, $列取甲) || typeof value[$列取甲] !== "function" || Object.getOwnPropertySymbols(value).length !== 3) {
    $终甲();
  }
}
const $表型甲 = Symbol("$表型甲");
const $表行型甲 = Symbol("$表行型甲");
const $表行槽甲 = Symbol("$表行槽甲");
const $表列槽甲 = Symbol("$表列槽甲");
const $表列符甲 = Symbol("$表列符甲");
function $表验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, $表型甲) || value[$表型甲] !== $表型甲 || !Object.prototype.hasOwnProperty.call(value, $表行槽甲) || typeof value[$表行槽甲] !== "function" || Object.getOwnPropertySymbols(value).length !== 3) {
    $终甲();
  }
  if (!Object.prototype.hasOwnProperty.call(value, $表列槽甲)) { $终甲(); }
}
function $表行验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, $表行型甲) || value[$表行型甲] !== $表行型甲 || !Object.prototype.hasOwnProperty.call(value, $表行表甲) || !Object.prototype.hasOwnProperty.call(value, $表行序甲) || !Number.isInteger(value[$表行序甲]) || Object.getOwnPropertySymbols(value).length !== 3) {
    $终甲();
  }
  $表验甲(value[$表行表甲]);
}
function $表行造甲(tableValue, index) {
  const row = Object.create(null);
  Object.defineProperties(row, {
    [$表行型甲]: { value: $表行型甲, writable: false, enumerable: false, configurable: false },
    [$表行表甲]: { value: tableValue, writable: false, enumerable: false, configurable: false },
    [$表行序甲]: { value: index, writable: false, enumerable: false, configurable: false },
  });
  return Object.freeze(row);
}
function $表造甲() {
  const $表列存甲 = Object.freeze([]);
  const $表列值甲 = Object.create(null);
  Object.defineProperties($表列值甲, {
    [$列型甲]: { value: $列型甲, writable: false, enumerable: false, configurable: false },
    [$表列符甲]: { value: $表列符甲, writable: false, enumerable: false, configurable: false },
    [$列取甲]: { value: (index) => {
      if (!Number.isFinite(index) || !Number.isInteger(index)) {
        return $值造甲($果型甲, "err", [$值造甲($枚型甲, "InvalidIndex", [index])]);
      }
      if (index < 0 || index >= 0) {
        return $值造甲($果型甲, "err", [$值造甲($枚型甲, "OutOfBounds", [index, 0])]);
      }
      return $值造甲($果型甲, "ok", [$表列存甲[index]]);
    }, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze($表列值甲);
  const value = Object.create(null);
  Object.defineProperties(value, {
    [$表型甲]: { value: $表型甲, writable: false, enumerable: false, configurable: false },
    [$表行槽甲]: { value: (index) => {
      if (!Number.isFinite(index) || !Number.isInteger(index)) {
        return $值造甲($果型乙, "err", [$值造甲($枚型甲, "InvalidIndex", [index])]);
      }
      if (index < 0 || index >= 0) {
        return $值造甲($果型乙, "err", [$值造甲($枚型甲, "OutOfBounds", [index, 0])]);
      }
      return $值造甲($果型乙, "ok", [$表行造甲(value, index)]);
    }, writable: false, enumerable: false, configurable: false },
    [$表列槽甲]: { value: $表列值甲, writable: false, enumerable: false, configurable: false },
  });
  return Object.freeze(value);
}
const $表值甲 = $表造甲();
const $表型乙 = Symbol("$表型乙");
const $表行型乙 = Symbol("$表行型乙");
const $表行槽乙 = Symbol("$表行槽乙");
const $表列槽乙 = Symbol("$表列槽乙");
const $表列符乙 = Symbol("$表列符乙");
const $表列槽丙 = Symbol("$表列槽丙");
const $表列符丙 = Symbol("$表列符丙");
function $表验乙(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, $表型乙) || value[$表型乙] !== $表型乙 || !Object.prototype.hasOwnProperty.call(value, $表行槽乙) || typeof value[$表行槽乙] !== "function" || Object.getOwnPropertySymbols(value).length !== 4) {
    $终甲();
  }
  if (!Object.prototype.hasOwnProperty.call(value, $表列槽乙)) { $终甲(); }
  if (!Object.prototype.hasOwnProperty.call(value, $表列槽丙)) { $终甲(); }
}
function $表行验乙(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, $表行型乙) || value[$表行型乙] !== $表行型乙 || !Object.prototype.hasOwnProperty.call(value, $表行表甲) || !Object.prototype.hasOwnProperty.call(value, $表行序甲) || !Number.isInteger(value[$表行序甲]) || Object.getOwnPropertySymbols(value).length !== 3) {
    $终甲();
  }
  $表验乙(value[$表行表甲]);
}
function $表行造乙(tableValue, index) {
  const row = Object.create(null);
  Object.defineProperties(row, {
    [$表行型乙]: { value: $表行型乙, writable: false, enumerable: false, configurable: false },
    [$表行表甲]: { value: tableValue, writable: false, enumerable: false, configurable: false },
    [$表行序甲]: { value: index, writable: false, enumerable: false, configurable: false },
  });
  return Object.freeze(row);
}
function $表造乙() {
  const $表列存乙 = Object.freeze([-0, 2]);
  const $表列值乙 = Object.create(null);
  Object.defineProperties($表列值乙, {
    [$列型甲]: { value: $列型甲, writable: false, enumerable: false, configurable: false },
    [$表列符乙]: { value: $表列符乙, writable: false, enumerable: false, configurable: false },
    [$列取甲]: { value: (index) => {
      if (!Number.isFinite(index) || !Number.isInteger(index)) {
        return $值造甲($果型甲, "err", [$值造甲($枚型甲, "InvalidIndex", [index])]);
      }
      if (index < 0 || index >= 2) {
        return $值造甲($果型甲, "err", [$值造甲($枚型甲, "OutOfBounds", [index, 2])]);
      }
      return $值造甲($果型甲, "ok", [$表列存乙[index]]);
    }, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze($表列值乙);
  const $表列存丙 = Object.freeze(["zero", "two"]);
  const $表列值丙 = Object.create(null);
  Object.defineProperties($表列值丙, {
    [$列型甲]: { value: $列型甲, writable: false, enumerable: false, configurable: false },
    [$表列符丙]: { value: $表列符丙, writable: false, enumerable: false, configurable: false },
    [$列取甲]: { value: (index) => {
      if (!Number.isFinite(index) || !Number.isInteger(index)) {
        return $值造甲($果型丙, "err", [$值造甲($枚型甲, "InvalidIndex", [index])]);
      }
      if (index < 0 || index >= 2) {
        return $值造甲($果型丙, "err", [$值造甲($枚型甲, "OutOfBounds", [index, 2])]);
      }
      return $值造甲($果型丙, "ok", [$表列存丙[index]]);
    }, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze($表列值丙);
  const value = Object.create(null);
  Object.defineProperties(value, {
    [$表型乙]: { value: $表型乙, writable: false, enumerable: false, configurable: false },
    [$表行槽乙]: { value: (index) => {
      if (!Number.isFinite(index) || !Number.isInteger(index)) {
        return $值造甲($果型丁, "err", [$值造甲($枚型甲, "InvalidIndex", [index])]);
      }
      if (index < 0 || index >= 2) {
        return $值造甲($果型丁, "err", [$值造甲($枚型甲, "OutOfBounds", [index, 2])]);
      }
      return $值造甲($果型丁, "ok", [$表行造乙(value, index)]);
    }, writable: false, enumerable: false, configurable: false },
    [$表列槽乙]: { value: $表列值乙, writable: false, enumerable: false, configurable: false },
    [$表列槽丙]: { value: $表列值丙, writable: false, enumerable: false, configurable: false },
  });
  return Object.freeze(value);
}
const $表值乙 = $表造乙();
function main() {
  const $临甲 = $表值乙;
  const $临乙 = 1;
  $表验乙($临甲);
  const $临丙 = $临甲[$表行槽乙]($临乙);
  $果验丁($临丙);
  const $临丁 = $临丙;
  $果验丁($临丁);
  if ($临丁.$tag === "err") {
    $终解甲($临丁.$payload[0]);
  }
  const row = $临丁.$payload[0];
  const $临戊 = row;
  $表行验乙($临戊);
  const $临己 = $临戊[$表行表甲];
  const $临庚 = $临己[$表列槽乙][$列取甲]($临戊[$表行序甲]);
  $果验甲($临庚);
  if ($临庚.$tag !== "ok") { $终甲(); }
  return $临庚.$payload[0];
}
function bounds(index) {
  const $临辛 = $表值乙;
  $表验乙($临辛);
  const $临壬 = $临辛[$表列槽乙];
  const $临癸 = index;
  $列验甲($临壬);
  const $临甲甲 = $临壬[$列取甲]($临癸);
  $果验甲($临甲甲);
  const $临甲乙 = $临甲甲;
  $果验甲($临甲乙);
  let $临甲丙;
  switch ($临甲乙.$tag) {
    case "ok": {
      const value = $临甲乙.$payload[0];
      $临甲丙 = value;
      break;
    }
    case "err": {
      const error = $临甲乙.$payload[0];
      $临甲丙 = (() => { const $配临甲 = error; $枚验甲($配临甲); switch ($配临甲.$tag) { case "InvalidIndex": { const value = $配临甲.$payload[0]; return 10; } case "OutOfBounds": { const value = $配临甲.$payload[0]; const rowCount = $配临甲.$payload[1]; return rowCount; } default: return $终甲(); } })();
      break;
    }
    default:
      $终甲();
  }
  return $临甲丙;
}
