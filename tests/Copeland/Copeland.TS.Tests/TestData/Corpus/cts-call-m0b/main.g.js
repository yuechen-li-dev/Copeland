"use strict";

const __cope_callable_instances = new WeakSet();
const __cope_callable_signatures = new WeakMap();
const __cope_callable_codes = new WeakMap();
function __cope_callable_ref(signature, code) {
    const carrier = Object.create(null);
    __cope_callable_signatures.set(carrier, signature);
    __cope_callable_codes.set(carrier, code);
    __cope_callable_instances.add(carrier);
    return Object.freeze(carrier);
}
function __cope_callable_invoke(carrier, signature, argumentsInOrder) {
    if (!__cope_callable_instances.has(carrier) || __cope_callable_signatures.get(carrier) !== signature) throw new Error("COPE-PANIC-CALLABLE: invalid callable");
    const code = __cope_callable_codes.get(carrier);
    return code(...argumentsInOrder);
}

function increment(value) {
    return (value + 1);
}

function apply(operation, value) {
    return __cope_callable_invoke(operation, "(named:number)->named:number", [value]);
}

function provide() {
    return __cope_callable_ref("(named:number)->named:number", increment);
}

function identity__primitive_number__8F3BAE8FF0D9F338(value) {
    return value;
}

function main() {
    const first = __cope_callable_ref("(named:number)->named:number", increment);
    const second = __cope_callable_ref("(named:number)->named:number", identity__primitive_number__8F3BAE8FF0D9F338);
    const supplied = provide();
    return (apply(first, 20) + __cope_callable_invoke(second, "(named:number)->named:number", [__cope_callable_invoke(supplied, "(named:number)->named:number", [20])]));
}
