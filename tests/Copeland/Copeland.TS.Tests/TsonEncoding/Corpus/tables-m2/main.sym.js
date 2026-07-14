"use strict";
function $终甲() {
  throw new Error("Copeland JavaScript backend invariant failure.");
}
function $值造甲(type, tag, payload) {
  const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
  if (type === $枚型甲) $枚印甲.add(value);
  if (type === $枚型乙) $枚印乙.add(value);
  if (type === $枚型丙) $枚印丙.add(value);
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
const $枚型丙 = Object.freeze(Object.create(null));
const $枚印丙 = new WeakSet();
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
function $枚验丙(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !$枚印丙.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $枚型丙 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
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
      if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "string")) {
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
const $果型戊 = Object.freeze(Object.create(null));
const $果型己 = Object.freeze(Object.create(null));
const $果型庚 = Object.freeze(Object.create(null));
const $果型辛 = Object.freeze(Object.create(null));
const $果型壬 = Object.freeze(Object.create(null));
function $果验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $果型甲 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
    $终甲();
  }
  switch (value.$tag) {
    case "ok":
      if (!(typeof value.$payload[0] === "boolean")) { $终甲(); }
      return;
    case "err":
      if (!(($枚验乙(value.$payload[0]), true))) { $终甲(); }
      return;
    default:
      $终甲();
  }
}
function $果验乙(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $果型乙 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
    $终甲();
  }
  switch (value.$tag) {
    case "ok":
      if (!(typeof value.$payload[0] === "string")) { $终甲(); }
      return;
    case "err":
      if (!(($枚验乙(value.$payload[0]), true))) { $终甲(); }
      return;
    default:
      $终甲();
  }
}
function $果验丙(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $果型丙 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
    $终甲();
  }
  switch (value.$tag) {
    case "ok":
      if (!(($表行验甲(value.$payload[0]), true))) { $终甲(); }
      return;
    case "err":
      if (!(($枚验乙(value.$payload[0]), true))) { $终甲(); }
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
      if (!(typeof value.$payload[0] === "number")) { $终甲(); }
      return;
    case "err":
      if (!(($枚验乙(value.$payload[0]), true))) { $终甲(); }
      return;
    default:
      $终甲();
  }
}
function $果验戊(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $果型戊 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
    $终甲();
  }
  switch (value.$tag) {
    case "ok":
      if (!(($录验甲(value.$payload[0]), true))) { $终甲(); }
      return;
    case "err":
      if (!(($枚验乙(value.$payload[0]), true))) { $终甲(); }
      return;
    default:
      $终甲();
  }
}
function $果验己(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $果型己 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
    $终甲();
  }
  switch (value.$tag) {
    case "ok":
      if (!(($枚验丙(value.$payload[0]), true))) { $终甲(); }
      return;
    case "err":
      if (!(($枚验乙(value.$payload[0]), true))) { $终甲(); }
      return;
    default:
      $终甲();
  }
}
function $果验庚(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $果型庚 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
    $终甲();
  }
  switch (value.$tag) {
    case "ok":
      if (!(Array.isArray(value.$payload[0]))) { $终甲(); }
      return;
    case "err":
      if (!(($枚验乙(value.$payload[0]), true))) { $终甲(); }
      return;
    default:
      $终甲();
  }
}
function $果验辛(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $果型辛 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
    $终甲();
  }
  switch (value.$tag) {
    case "ok":
      if (!(($表行验乙(value.$payload[0]), true))) { $终甲(); }
      return;
    case "err":
      if (!(($枚验乙(value.$payload[0]), true))) { $终甲(); }
      return;
    default:
      $终甲();
  }
}
function $果验壬(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== $果型壬 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload) || value.$payload.length !== 1 || !Object.prototype.hasOwnProperty.call(value.$payload, 0)) {
    $终甲();
  }
  switch (value.$tag) {
    case "ok":
      if (!(typeof value.$payload[0] === "string")) { $终甲(); }
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
const $列印甲 = new WeakSet();
const $列值甲 = Symbol("$列值甲");
const $表行表甲 = Symbol("$表行表甲");
const $表行序甲 = Symbol("$表行序甲");
function $列验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, $列型甲) || value[$列型甲] !== $列型甲 || !Object.prototype.hasOwnProperty.call(value, $列取甲) || typeof value[$列取甲] !== "function" || !$列印甲.has(value) || !Object.prototype.hasOwnProperty.call(value, $列值甲) || !Array.isArray(value[$列值甲]) || !Object.isFrozen(value[$列值甲])) {
    $终甲();
  }
}
const $表型甲 = Symbol("$表型甲");
const $表印甲 = new WeakSet();
const $表行型甲 = Symbol("$表行型甲");
const $表行槽甲 = Symbol("$表行槽甲");
const $表列槽甲 = Symbol("$表列槽甲");
const $表列符甲 = Symbol("$表列符甲");
const $表列槽乙 = Symbol("$表列槽乙");
const $表列符乙 = Symbol("$表列符乙");
function $表验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !$表印甲.has(value) || !Object.prototype.hasOwnProperty.call(value, $表型甲) || value[$表型甲] !== $表型甲 || !Object.prototype.hasOwnProperty.call(value, $表行槽甲) || typeof value[$表行槽甲] !== "function") {
    $终甲();
  }
  if (!Object.prototype.hasOwnProperty.call(value, $表列槽甲)) { $终甲(); }
  if (!Object.prototype.hasOwnProperty.call(value, $表列槽乙)) { $终甲(); }
}
function $表行验甲(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, $表行型甲) || value[$表行型甲] !== $表行型甲 || !Object.prototype.hasOwnProperty.call(value, $表行表甲) || !Object.prototype.hasOwnProperty.call(value, $表行序甲) || !Number.isInteger(value[$表行序甲])) {
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
    [$列值甲]: { value: $表列存甲, writable: false, enumerable: false, configurable: false },
    [$列取甲]: { value: (index) => {
      if (!Number.isFinite(index) || !Number.isInteger(index)) {
        return $值造甲($果型甲, "err", [$值造甲($枚型乙, "InvalidIndex", [index])]);
      }
      if (index < 0 || index >= 0) {
        return $值造甲($果型甲, "err", [$值造甲($枚型乙, "OutOfBounds", [index, 0])]);
      }
      return $值造甲($果型甲, "ok", [$表列存甲[index]]);
    }, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze($表列值甲);
  $列印甲.add($表列值甲);
  const $表列存乙 = Object.freeze([]);
  const $表列值乙 = Object.create(null);
  Object.defineProperties($表列值乙, {
    [$列型甲]: { value: $列型甲, writable: false, enumerable: false, configurable: false },
    [$表列符乙]: { value: $表列符乙, writable: false, enumerable: false, configurable: false },
    [$列值甲]: { value: $表列存乙, writable: false, enumerable: false, configurable: false },
    [$列取甲]: { value: (index) => {
      if (!Number.isFinite(index) || !Number.isInteger(index)) {
        return $值造甲($果型乙, "err", [$值造甲($枚型乙, "InvalidIndex", [index])]);
      }
      if (index < 0 || index >= 0) {
        return $值造甲($果型乙, "err", [$值造甲($枚型乙, "OutOfBounds", [index, 0])]);
      }
      return $值造甲($果型乙, "ok", [$表列存乙[index]]);
    }, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze($表列值乙);
  $列印甲.add($表列值乙);
  const value = Object.create(null);
  Object.defineProperties(value, {
    [$表型甲]: { value: $表型甲, writable: false, enumerable: false, configurable: false },
    [$表行槽甲]: { value: (index) => {
      if (!Number.isFinite(index) || !Number.isInteger(index)) {
        return $值造甲($果型丙, "err", [$值造甲($枚型乙, "InvalidIndex", [index])]);
      }
      if (index < 0 || index >= 0) {
        return $值造甲($果型丙, "err", [$值造甲($枚型乙, "OutOfBounds", [index, 0])]);
      }
      return $值造甲($果型丙, "ok", [$表行造甲(value, index)]);
    }, writable: false, enumerable: false, configurable: false },
    [$表列槽甲]: { value: $表列值甲, writable: false, enumerable: false, configurable: false },
    [$表列槽乙]: { value: $表列值乙, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze(value);
  $表印甲.add(value);
  return value;
}
const $表值甲 = $表造甲();
const $表型乙 = Symbol("$表型乙");
const $表印乙 = new WeakSet();
const $表行型乙 = Symbol("$表行型乙");
const $表行槽乙 = Symbol("$表行槽乙");
const $表列槽丙 = Symbol("$表列槽丙");
const $表列符丙 = Symbol("$表列符丙");
const $表列槽丁 = Symbol("$表列槽丁");
const $表列符丁 = Symbol("$表列符丁");
const $表列槽戊 = Symbol("$表列槽戊");
const $表列符戊 = Symbol("$表列符戊");
const $表列槽己 = Symbol("$表列槽己");
const $表列符己 = Symbol("$表列符己");
const $表列槽庚 = Symbol("$表列槽庚");
const $表列符庚 = Symbol("$表列符庚");
function $表验乙(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !$表印乙.has(value) || !Object.prototype.hasOwnProperty.call(value, $表型乙) || value[$表型乙] !== $表型乙 || !Object.prototype.hasOwnProperty.call(value, $表行槽乙) || typeof value[$表行槽乙] !== "function") {
    $终甲();
  }
  if (!Object.prototype.hasOwnProperty.call(value, $表列槽丙)) { $终甲(); }
  if (!Object.prototype.hasOwnProperty.call(value, $表列槽丁)) { $终甲(); }
  if (!Object.prototype.hasOwnProperty.call(value, $表列槽戊)) { $终甲(); }
  if (!Object.prototype.hasOwnProperty.call(value, $表列槽己)) { $终甲(); }
  if (!Object.prototype.hasOwnProperty.call(value, $表列槽庚)) { $终甲(); }
}
function $表行验乙(value) {
  if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !Object.prototype.hasOwnProperty.call(value, $表行型乙) || value[$表行型乙] !== $表行型乙 || !Object.prototype.hasOwnProperty.call(value, $表行表甲) || !Object.prototype.hasOwnProperty.call(value, $表行序甲) || !Number.isInteger(value[$表行序甲])) {
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
  const $表列存丙 = Object.freeze([true, false, true, false, true]);
  const $表列值丙 = Object.create(null);
  Object.defineProperties($表列值丙, {
    [$列型甲]: { value: $列型甲, writable: false, enumerable: false, configurable: false },
    [$表列符丙]: { value: $表列符丙, writable: false, enumerable: false, configurable: false },
    [$列值甲]: { value: $表列存丙, writable: false, enumerable: false, configurable: false },
    [$列取甲]: { value: (index) => {
      if (!Number.isFinite(index) || !Number.isInteger(index)) {
        return $值造甲($果型甲, "err", [$值造甲($枚型乙, "InvalidIndex", [index])]);
      }
      if (index < 0 || index >= 5) {
        return $值造甲($果型甲, "err", [$值造甲($枚型乙, "OutOfBounds", [index, 5])]);
      }
      return $值造甲($果型甲, "ok", [$表列存丙[index]]);
    }, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze($表列值丙);
  $列印甲.add($表列值丙);
  const $表列存丁 = Object.freeze([0, -0, 1.5, NaN, -Infinity]);
  const $表列值丁 = Object.create(null);
  Object.defineProperties($表列值丁, {
    [$列型甲]: { value: $列型甲, writable: false, enumerable: false, configurable: false },
    [$表列符丁]: { value: $表列符丁, writable: false, enumerable: false, configurable: false },
    [$列值甲]: { value: $表列存丁, writable: false, enumerable: false, configurable: false },
    [$列取甲]: { value: (index) => {
      if (!Number.isFinite(index) || !Number.isInteger(index)) {
        return $值造甲($果型丁, "err", [$值造甲($枚型乙, "InvalidIndex", [index])]);
      }
      if (index < 0 || index >= 5) {
        return $值造甲($果型丁, "err", [$值造甲($枚型乙, "OutOfBounds", [index, 5])]);
      }
      return $值造甲($果型丁, "ok", [$表列存丁[index]]);
    }, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze($表列值丁);
  $列印甲.add($表列值丁);
  const $表列存戊 = Object.freeze([$录造甲("plain"), $录造甲("quote \" slash \\ newline\n"), $录造甲("雪"), $录造甲("\ud83d\ude00"), $录造甲("\ud801\udc37")]);
  const $表列值戊 = Object.create(null);
  Object.defineProperties($表列值戊, {
    [$列型甲]: { value: $列型甲, writable: false, enumerable: false, configurable: false },
    [$表列符戊]: { value: $表列符戊, writable: false, enumerable: false, configurable: false },
    [$列值甲]: { value: $表列存戊, writable: false, enumerable: false, configurable: false },
    [$列取甲]: { value: (index) => {
      if (!Number.isFinite(index) || !Number.isInteger(index)) {
        return $值造甲($果型戊, "err", [$值造甲($枚型乙, "InvalidIndex", [index])]);
      }
      if (index < 0 || index >= 5) {
        return $值造甲($果型戊, "err", [$值造甲($枚型乙, "OutOfBounds", [index, 5])]);
      }
      return $值造甲($果型戊, "ok", [$表列存戊[index]]);
    }, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze($表列值戊);
  $列印甲.add($表列值戊);
  const $表列存己 = Object.freeze([$值造甲($枚型丙, "Off", []), $值造甲($枚型丙, "Named", ["payload"]), $值造甲($枚型丙, "Named", ["雪"]), $值造甲($枚型丙, "Off", []), $值造甲($枚型丙, "Named", ["array"])]);
  const $表列值己 = Object.create(null);
  Object.defineProperties($表列值己, {
    [$列型甲]: { value: $列型甲, writable: false, enumerable: false, configurable: false },
    [$表列符己]: { value: $表列符己, writable: false, enumerable: false, configurable: false },
    [$列值甲]: { value: $表列存己, writable: false, enumerable: false, configurable: false },
    [$列取甲]: { value: (index) => {
      if (!Number.isFinite(index) || !Number.isInteger(index)) {
        return $值造甲($果型己, "err", [$值造甲($枚型乙, "InvalidIndex", [index])]);
      }
      if (index < 0 || index >= 5) {
        return $值造甲($果型己, "err", [$值造甲($枚型乙, "OutOfBounds", [index, 5])]);
      }
      return $值造甲($果型己, "ok", [$表列存己[index]]);
    }, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze($表列值己);
  $列印甲.add($表列值己);
  const $表列存庚 = Object.freeze([Object.freeze([]), Object.freeze([Object.freeze([1, 2]), Object.freeze([])]), Object.freeze([Object.freeze([]), Object.freeze([0])]), Object.freeze([Object.freeze([Infinity])]), Object.freeze([Object.freeze([NaN])])]);
  const $表列值庚 = Object.create(null);
  Object.defineProperties($表列值庚, {
    [$列型甲]: { value: $列型甲, writable: false, enumerable: false, configurable: false },
    [$表列符庚]: { value: $表列符庚, writable: false, enumerable: false, configurable: false },
    [$列值甲]: { value: $表列存庚, writable: false, enumerable: false, configurable: false },
    [$列取甲]: { value: (index) => {
      if (!Number.isFinite(index) || !Number.isInteger(index)) {
        return $值造甲($果型庚, "err", [$值造甲($枚型乙, "InvalidIndex", [index])]);
      }
      if (index < 0 || index >= 5) {
        return $值造甲($果型庚, "err", [$值造甲($枚型乙, "OutOfBounds", [index, 5])]);
      }
      return $值造甲($果型庚, "ok", [$表列存庚[index]]);
    }, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze($表列值庚);
  $列印甲.add($表列值庚);
  const value = Object.create(null);
  Object.defineProperties(value, {
    [$表型乙]: { value: $表型乙, writable: false, enumerable: false, configurable: false },
    [$表行槽乙]: { value: (index) => {
      if (!Number.isFinite(index) || !Number.isInteger(index)) {
        return $值造甲($果型辛, "err", [$值造甲($枚型乙, "InvalidIndex", [index])]);
      }
      if (index < 0 || index >= 5) {
        return $值造甲($果型辛, "err", [$值造甲($枚型乙, "OutOfBounds", [index, 5])]);
      }
      return $值造甲($果型辛, "ok", [$表行造乙(value, index)]);
    }, writable: false, enumerable: false, configurable: false },
    [$表列槽丙]: { value: $表列值丙, writable: false, enumerable: false, configurable: false },
    [$表列槽丁]: { value: $表列值丁, writable: false, enumerable: false, configurable: false },
    [$表列槽戊]: { value: $表列值戊, writable: false, enumerable: false, configurable: false },
    [$表列槽己]: { value: $表列值己, writable: false, enumerable: false, configurable: false },
    [$表列槽庚]: { value: $表列值庚, writable: false, enumerable: false, configurable: false },
  });
  Object.freeze(value);
  $表印乙.add(value);
  return value;
}
const $表值乙 = $表造乙();
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
      outputLimit: () => $写错甲("limit"),
      error: () => error,
      finish: () => parts.join(""),
    });
  }
  function $布写甲(writer, value, indentation) { return writer.static(value ? "true" : "false"); }
  function $数写甲(writer, value, indentation) { return writer.number(value); }
  function $串写甲(writer, value, indentation) { return writer.string(value); }
  function $录写甲(writer, value, indentation) {
    $录验甲(value);
    if (!writer.static("$record.Point({\n")) return false;
    if (!writer.indent(indentation + 1)) return false;
    if (!writer.static("\"name\": ")) return false;
    if (!$串写甲(writer, value[$录域甲], indentation + 1)) return false;
    if (!writer.static(",\n")) return false;
    if (!writer.indent(indentation)) return false;
    return writer.static("})");
  }
  function $枚写甲(writer, value, indentation) {
    $枚验丙(value);
    switch (value.$tag) {
      case "Off":
        return writer.static("State.Off");
      case "Named":
        if (!writer.static("State.Named(\n")) return false;
        if (!writer.indent(indentation + 1)) return false;
        if (!$串写甲(writer, value.$payload[0], indentation + 1)) return false;
        if (!writer.static("\n")) return false;
        if (!writer.indent(indentation)) return false;
        return writer.static(")");
      default:
        $终甲();
    }
  }
  function $组写甲(writer, value, indentation) {
    const array = value;
    if (!Array.isArray(array)) { $终甲(); }
    const length = array.length;
    if (length > 100000) return writer.outputLimit();
    if (length === 0) return writer.static("[]");
    if (!writer.static("[\n")) return false;
    for (let index = 0; index < length; index += 1) {
      if (!Object.prototype.hasOwnProperty.call(array, index)) { $终甲(); }
      const element = array[index];
      if (!Array.isArray(element)) { $终甲(); }
      if (!writer.indent(indentation + 1)) return false;
      if (!$组写乙(writer, element, indentation + 1)) return false;
      if (!writer.static(",\n")) return false;
    }
    if (!writer.indent(indentation)) return false;
    return writer.static("]");
  }
  function $组写乙(writer, value, indentation) {
    const array = value;
    if (!Array.isArray(array)) { $终甲(); }
    const length = array.length;
    if (length > 100000) return writer.outputLimit();
    if (length === 0) return writer.static("[]");
    if (!writer.static("[\n")) return false;
    for (let index = 0; index < length; index += 1) {
      if (!Object.prototype.hasOwnProperty.call(array, index)) { $终甲(); }
      const element = array[index];
      if (typeof element !== "number") { $终甲(); }
      if (!writer.indent(indentation + 1)) return false;
      if (!$数写甲(writer, element, indentation + 1)) return false;
      if (!writer.static(",\n")) return false;
    }
    if (!writer.indent(indentation)) return false;
    return writer.static("]");
  }
  function $编甲(value) {
    $表验乙(value);
    const column0 = value[$表列槽丙];
    $列验甲(column0);
    if (column0[$表列符丙] !== $表列符丙) { $终甲(); }
    const cells0 = column0[$列值甲];
    const length0 = cells0.length;
    if (length0 !== 5) { $终甲(); }
    const column1 = value[$表列槽丁];
    $列验甲(column1);
    if (column1[$表列符丁] !== $表列符丁) { $终甲(); }
    const cells1 = column1[$列值甲];
    const length1 = cells1.length;
    if (length1 !== 5) { $终甲(); }
    const column2 = value[$表列槽戊];
    $列验甲(column2);
    if (column2[$表列符戊] !== $表列符戊) { $终甲(); }
    const cells2 = column2[$列值甲];
    const length2 = cells2.length;
    if (length2 !== 5) { $终甲(); }
    const column3 = value[$表列槽己];
    $列验甲(column3);
    if (column3[$表列符己] !== $表列符己) { $终甲(); }
    const cells3 = column3[$列值甲];
    const length3 = cells3.length;
    if (length3 !== 5) { $终甲(); }
    const column4 = value[$表列槽庚];
    $列验甲(column4);
    if (column4[$表列符庚] !== $表列符庚) { $终甲(); }
    const cells4 = column4[$列值甲];
    const length4 = cells4.length;
    if (length4 !== 5) { $终甲(); }
    const writer = $写造甲(1048576, 262144);
    if (!writer.static("const $schema: string = \"copeland://corpus/runtime-table-encoding\";\n\nrecord Point {\n    name: string;\n}\n\nrecord table Samples {\n")) {
      const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
      const error = $值造甲($枚型甲, tag, []);
      return $值造甲($果型壬, "err", [error]);
    }
    if (length0 === 0) {
      if (!writer.static("    active: boolean = ") || !writer.static("[];\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
    } else {
      if (!writer.static("    active: boolean = ") || !writer.static("[\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
      for (let index = 0; index < length0; index += 1) {
        if (!Object.prototype.hasOwnProperty.call(cells0, index)) { $终甲(); }
        const cell = cells0[index];
        if (!writer.indent(2) || !$布写甲(writer, cell, 2) || !writer.static(",\n")) {
          const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
          const error = $值造甲($枚型甲, tag, []);
          return $值造甲($果型壬, "err", [error]);
        }
      }
      if (!writer.indent(1) || !writer.static("];\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
    }
    if (length1 === 0) {
      if (!writer.static("    score: number = ") || !writer.static("[];\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
    } else {
      if (!writer.static("    score: number = ") || !writer.static("[\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
      for (let index = 0; index < length1; index += 1) {
        if (!Object.prototype.hasOwnProperty.call(cells1, index)) { $终甲(); }
        const cell = cells1[index];
        if (!writer.indent(2) || !$数写甲(writer, cell, 2) || !writer.static(",\n")) {
          const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
          const error = $值造甲($枚型甲, tag, []);
          return $值造甲($果型壬, "err", [error]);
        }
      }
      if (!writer.indent(1) || !writer.static("];\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
    }
    if (length2 === 0) {
      if (!writer.static("    point: Point = ") || !writer.static("[];\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
    } else {
      if (!writer.static("    point: Point = ") || !writer.static("[\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
      for (let index = 0; index < length2; index += 1) {
        if (!Object.prototype.hasOwnProperty.call(cells2, index)) { $终甲(); }
        const cell = cells2[index];
        if (!writer.indent(2) || !$录写甲(writer, cell, 2) || !writer.static(",\n")) {
          const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
          const error = $值造甲($枚型甲, tag, []);
          return $值造甲($果型壬, "err", [error]);
        }
      }
      if (!writer.indent(1) || !writer.static("];\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
    }
    if (length3 === 0) {
      if (!writer.static("    state: State = ") || !writer.static("[];\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
    } else {
      if (!writer.static("    state: State = ") || !writer.static("[\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
      for (let index = 0; index < length3; index += 1) {
        if (!Object.prototype.hasOwnProperty.call(cells3, index)) { $终甲(); }
        const cell = cells3[index];
        if (!writer.indent(2) || !$枚写甲(writer, cell, 2) || !writer.static(",\n")) {
          const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
          const error = $值造甲($枚型甲, tag, []);
          return $值造甲($果型壬, "err", [error]);
        }
      }
      if (!writer.indent(1) || !writer.static("];\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
    }
    if (length4 === 0) {
      if (!writer.static("    values: number[][] = ") || !writer.static("[];\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
    } else {
      if (!writer.static("    values: number[][] = ") || !writer.static("[\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
      for (let index = 0; index < length4; index += 1) {
        if (!Object.prototype.hasOwnProperty.call(cells4, index)) { $终甲(); }
        const cell = cells4[index];
        if (!writer.indent(2) || !$组写甲(writer, cell, 2) || !writer.static(",\n")) {
          const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
          const error = $值造甲($枚型甲, tag, []);
          return $值造甲($果型壬, "err", [error]);
        }
      }
      if (!writer.indent(1) || !writer.static("];\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
    }
    if (!writer.static("}\n\nenum State {\n    Off,\n    Named(label: string),\n}\n\nconst $value = Samples;\n")) {
      const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
      const error = $值造甲($枚型甲, tag, []);
      return $值造甲($果型壬, "err", [error]);
    }
    return $值造甲($果型壬, "ok", [writer.finish()]);
  }
  function $编乙(value) {
    $表验甲(value);
    const column0 = value[$表列槽甲];
    $列验甲(column0);
    if (column0[$表列符甲] !== $表列符甲) { $终甲(); }
    const cells0 = column0[$列值甲];
    const length0 = cells0.length;
    if (length0 !== 0) { $终甲(); }
    const column1 = value[$表列槽乙];
    $列验甲(column1);
    if (column1[$表列符乙] !== $表列符乙) { $终甲(); }
    const cells1 = column1[$列值甲];
    const length1 = cells1.length;
    if (length1 !== 0) { $终甲(); }
    const writer = $写造甲(1048576, 262144);
    if (!writer.static("const $schema: string = \"copeland://corpus/runtime-table-encoding\";\n\nrecord table Empty {\n")) {
      const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
      const error = $值造甲($枚型甲, tag, []);
      return $值造甲($果型壬, "err", [error]);
    }
    if (length0 === 0) {
      if (!writer.static("    active: boolean = ") || !writer.static("[];\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
    } else {
      if (!writer.static("    active: boolean = ") || !writer.static("[\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
      for (let index = 0; index < length0; index += 1) {
        if (!Object.prototype.hasOwnProperty.call(cells0, index)) { $终甲(); }
        const cell = cells0[index];
        if (!writer.indent(2) || !$布写甲(writer, cell, 2) || !writer.static(",\n")) {
          const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
          const error = $值造甲($枚型甲, tag, []);
          return $值造甲($果型壬, "err", [error]);
        }
      }
      if (!writer.indent(1) || !writer.static("];\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
    }
    if (length1 === 0) {
      if (!writer.static("    note: string = ") || !writer.static("[];\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
    } else {
      if (!writer.static("    note: string = ") || !writer.static("[\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
      for (let index = 0; index < length1; index += 1) {
        if (!Object.prototype.hasOwnProperty.call(cells1, index)) { $终甲(); }
        const cell = cells1[index];
        if (!writer.indent(2) || !$串写甲(writer, cell, 2) || !writer.static(",\n")) {
          const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
          const error = $值造甲($枚型甲, tag, []);
          return $值造甲($果型壬, "err", [error]);
        }
      }
      if (!writer.indent(1) || !writer.static("];\n")) {
        const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
        const error = $值造甲($枚型甲, tag, []);
        return $值造甲($果型壬, "err", [error]);
      }
    }
    if (!writer.static("}\n\nconst $value = Empty;\n")) {
      const tag = writer.error() === "invalid" ? "InvalidUnicode" : "OutputLimitExceeded";
      const error = $值造甲($枚型甲, tag, []);
      return $值造甲($果型壬, "err", [error]);
    }
    return $值造甲($果型壬, "ok", [writer.finish()]);
  }
  const api = Object.create(null);
  Object.defineProperty(api, "tson0", { value: $编甲, writable: false, enumerable: false, configurable: false });
  Object.defineProperty(api, "tson1", { value: $编乙, writable: false, enumerable: false, configurable: false });
  return Object.freeze(api);
})();
function encode() {
  return $运编甲["tson0"]($表值乙);
}
function encodeEmpty() {
  return $运编甲["tson1"]($表值甲);
}
