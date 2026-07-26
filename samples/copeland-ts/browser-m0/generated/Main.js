import { onClick } from "@copeland/browser-m0";
import { setText } from "@copeland/browser-m0";
import { Increment } from "./Counter.js";
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



function __cope_arrow_0(countElement, current) {
    const next = Increment(current);
    setText(countElement, String(next));
    return next;
}

function Main() {
    const countElement = "count";
    setText(countElement, "0");
    onClick("increment", (...args) => __cope_callable_invoke(__cope_callable_capture("(named:int)->named:int", __cope_arrow_0, [countElement]), "(named:int)->named:int", args));
}
export { Main };
