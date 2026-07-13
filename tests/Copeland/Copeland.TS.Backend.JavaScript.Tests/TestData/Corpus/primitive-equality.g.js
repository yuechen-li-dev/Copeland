"use strict";

function booleanEqual() {
    return (true === true);
}

function booleanNotEqual() {
    return (true !== false);
}

function numberEqual() {
    return (42 === 42);
}

function numberNotEqual() {
    return (42 !== 41);
}

function nanEqual() {
    const nan = (0 / 0);
    return (nan === nan);
}

function nanNotEqual() {
    const nan = (0 / 0);
    return (nan !== nan);
}

function signedZeroEqual() {
    const positiveZero = 0;
    const negativeZero = (0 * (0 - 1));
    return (positiveZero === negativeZero);
}

function signedZeroNotEqual() {
    const positiveZero = 0;
    const negativeZero = (0 * (0 - 1));
    return (positiveZero !== negativeZero);
}
