"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_make_1(type, tag, payload) {
    const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
    if (type === __cope_m3_type_16) __cope_m3_instances_17.add(value);
    return value;
}

const __cope_m3_record_type_r1_2 = Symbol("r1");
const __cope_m3_record_instances_r1_3 = new WeakSet();
const __cope_m3_record_field___cope_00720031002e00660030_6 = Symbol("r1.f0");
const __cope_m3_record_field___cope_00720031002e00660031_7 = Symbol("r1.f1");

function __cope_m3_record_make_r1_4(field0, field1) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r1_2]: { value: __cope_m3_record_type_r1_2, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660030_6]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720031002e00660031_7]: { value: field1, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r1_3.add(value);
    return value;
}

function __cope_m3_record_require_r1_5(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r1_3.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r1_2) || value[__cope_m3_record_type_r1_2] !== __cope_m3_record_type_r1_2 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660030_6) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720031002e00660031_7)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_record_type_r2_8 = Symbol("r2");
const __cope_m3_record_instances_r2_9 = new WeakSet();
const __cope_m3_record_field___cope_00720032002e00660030_12 = Symbol("r2.f0");
const __cope_m3_record_field___cope_00720032002e00660031_13 = Symbol("r2.f1");
const __cope_m3_record_field___cope_00720032002e00660032_14 = Symbol("r2.f2");
const __cope_m3_record_field___cope_00720032002e00660033_15 = Symbol("r2.f3");

function __cope_m3_record_make_r2_10(field0, field1, field2, field3) {
    const value = Object.create(null);
    Object.defineProperties(value, {
        [__cope_m3_record_type_r2_8]: { value: __cope_m3_record_type_r2_8, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660030_12]: { value: field0, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660031_13]: { value: field1, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660032_14]: { value: field2, writable: false, enumerable: false, configurable: false },
        [__cope_m3_record_field___cope_00720032002e00660033_15]: { value: field3, writable: false, enumerable: false, configurable: false },
    });
    Object.freeze(value);
    __cope_m3_record_instances_r2_9.add(value);
    return value;
}

function __cope_m3_record_require_r2_11(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_record_instances_r2_9.has(value) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_type_r2_8) || value[__cope_m3_record_type_r2_8] !== __cope_m3_record_type_r2_8 || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660030_12) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660031_13) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660032_14) || !Object.prototype.hasOwnProperty.call(value, __cope_m3_record_field___cope_00720032002e00660033_15)) {
        __cope_m3_panic_0();
    }
}

const __cope_m3_type_16 = Object.freeze(Object.create(null));
const __cope_m3_instances_17 = new WeakSet();

function __cope_m3_validate_18(value) {
    if (typeof value !== "object" || value === null || Object.getPrototypeOf(value) !== null || !Object.isFrozen(value) || !__cope_m3_instances_17.has(value) || !Object.prototype.hasOwnProperty.call(value, "$type") || !Object.prototype.hasOwnProperty.call(value, "$tag") || !Object.prototype.hasOwnProperty.call(value, "$payload") || value.$type !== __cope_m3_type_16 || typeof value.$tag !== "string" || !Array.isArray(value.$payload) || !Object.isFrozen(value.$payload)) {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Started":
            if (value.$payload.length !== 0) {
                __cope_m3_panic_0();
            }
            return;
        case "Retried":
            if (value.$payload.length !== 1) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "number")) {
                __cope_m3_panic_0();
            }
            return;
        case "Completed":
            if (value.$payload.length !== 1) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "number")) {
                __cope_m3_panic_0();
            }
            return;
        case "Failed":
            if (value.$payload.length !== 1) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value.$payload, 0) || !(typeof value.$payload[0] === "string")) {
                __cope_m3_panic_0();
            }
            return;
        default:
            __cope_m3_panic_0();
    }
}

function nextSequence(value) {
    return (value + 1);
}

function auditScore(entry) {
    const __cope_m3_record_receiver_19 = entry;
    __cope_m3_record_require_r1_5(__cope_m3_record_receiver_19);
    const __cope_m3_match_20 = __cope_m3_record_receiver_19[__cope_m3_record_field___cope_00720031002e00660030_6];
    __cope_m3_validate_18(__cope_m3_match_20);
    let __cope_m3_match_value_21;
    switch (__cope_m3_match_20.$tag) {
        case "Started":
        {
            const __cope_m3_record_receiver_22 = entry;
            __cope_m3_record_require_r1_5(__cope_m3_record_receiver_22);
            __cope_m3_match_value_21 = __cope_m3_record_receiver_22[__cope_m3_record_field___cope_00720031002e00660031_7];
            break;
        }
        case "Retried":
        {
            const count = __cope_m3_match_20.$payload[0];
            const __cope_m3_record_receiver_23 = entry;
            __cope_m3_record_require_r1_5(__cope_m3_record_receiver_23);
            __cope_m3_match_value_21 = (__cope_m3_record_receiver_23[__cope_m3_record_field___cope_00720031002e00660031_7] + count);
            break;
        }
        case "Completed":
        {
            const total = __cope_m3_match_20.$payload[0];
            const __cope_m3_record_receiver_24 = entry;
            __cope_m3_record_require_r1_5(__cope_m3_record_receiver_24);
            __cope_m3_match_value_21 = (__cope_m3_record_receiver_24[__cope_m3_record_field___cope_00720031002e00660031_7] + total);
            break;
        }
        case "Failed":
        {
            const reason = __cope_m3_match_20.$payload[0];
            const __cope_m3_record_receiver_25 = entry;
            __cope_m3_record_require_r1_5(__cope_m3_record_receiver_25);
            __cope_m3_match_value_21 = (__cope_m3_record_receiver_25[__cope_m3_record_field___cope_00720031002e00660031_7] + reason.length);
            break;
        }
        default:
            __cope_m3_panic_0();
    }
    return __cope_m3_match_value_21;
}

function main() {
    const __cope_m3_record_init_26 = __cope_m3_make_1(__cope_m3_type_16, "Completed", [42]);
    const __cope_m3_record_init_27 = 3;
    const entry = __cope_m3_record_make_r1_4(__cope_m3_record_init_26, __cope_m3_record_init_27);
    return auditScore(entry);
}

const Delivery = Object.freeze({
    start() {
        let board = { attempts: 0, total: 0, sequence: 0, accepted: false };
        let state = "Idle";
        let terminal = false;
        let sending = false;
        let revision = 0;
        const result = (kind, fromState, toState, event, error = null) => Object.freeze({ kind, fromState, toState, event, revision, terminal, error });
        const session = {
            get state() { return state; },
            get board() { return Object.freeze({ ...board }); },
            get terminal() { return terminal; },
            get revision() { return revision; },
            sendStart(amount) {
                if (sending) throw new Error("A Copeland flow session cannot receive a reentrant event.");
                if (terminal) return result("Terminal", state, null, "Start");
                sending = true;
                try {
                    switch (state) {
                        case "Idle": {
                            if (!((amount > 0))) return result("Unhandled", state, null, "Start");
                            let nextBoard = board;
                            nextBoard = { ...nextBoard, total: amount };
                            board = nextBoard;
                            nextBoard = { ...nextBoard, attempts: (board["attempts"] + 1) };
                            board = nextBoard;
                            nextBoard = { ...nextBoard, sequence: nextSequence(board["sequence"]) };
                            board = nextBoard;
                            const fromState = state;
                            board = nextBoard;
                            state = "Staging";
                            revision += 1;
                            return result("Transitioned", fromState, state, "Start");
                        }
                        default: return result("Unhandled", state, null, "Start");
                    }
                } finally {
                    sending = false;
                }
            },
            sendRetry(amount) {
                if (sending) throw new Error("A Copeland flow session cannot receive a reentrant event.");
                if (terminal) return result("Terminal", state, null, "Retry");
                sending = true;
                try {
                    switch (state) {
                        case "Staging": {
                            if (!((amount > 0))) return result("Unhandled", state, null, "Retry");
                            let nextBoard = board;
                            nextBoard = { ...nextBoard, attempts: (board["attempts"] + 1) };
                            board = nextBoard;
                            nextBoard = { ...nextBoard, total: (board["total"] + amount) };
                            board = nextBoard;
                            const fromState = state;
                            board = nextBoard;
                            state = "Retrying";
                            revision += 1;
                            return result("Transitioned", fromState, state, "Retry");
                        }
                        default: return result("Unhandled", state, null, "Retry");
                    }
                } finally {
                    sending = false;
                }
            },
            sendAccept(amount) {
                if (sending) throw new Error("A Copeland flow session cannot receive a reentrant event.");
                if (terminal) return result("Terminal", state, null, "Accept");
                sending = true;
                try {
                    switch (state) {
                        case "Staging": {
                            if (!((amount >= 0))) return result("Unhandled", state, null, "Accept");
                            let nextBoard = board;
                            nextBoard = { ...nextBoard, total: (board["total"] + amount) };
                            board = nextBoard;
                            nextBoard = { ...nextBoard, accepted: true };
                            board = nextBoard;
                            const fromState = state;
                            board = nextBoard;
                            state = "Accepted";
                            revision += 1;
                            return result("Transitioned", fromState, state, "Accept");
                        }
                        case "Retrying": {
                            if (!((amount >= 0))) return result("Unhandled", state, null, "Accept");
                            let nextBoard = board;
                            nextBoard = { ...nextBoard, total: (board["total"] + amount) };
                            board = nextBoard;
                            nextBoard = { ...nextBoard, accepted: true };
                            board = nextBoard;
                            const fromState = state;
                            board = nextBoard;
                            state = "Accepted";
                            revision += 1;
                            return result("Transitioned", fromState, state, "Accept");
                        }
                        case "Completing": {
                            let nextBoard = board;
                            nextBoard = { ...nextBoard, total: (board["total"] + amount) };
                            board = nextBoard;
                            const fromState = state;
                            board = nextBoard;
                            state = "Completed";
                            revision += 1;
                            terminal = true;
                            return result("Completed", fromState, state, "Accept", null);
                        }
                        default: return result("Unhandled", state, null, "Accept");
                    }
                } finally {
                    sending = false;
                }
            },
            sendReject(code) {
                if (sending) throw new Error("A Copeland flow session cannot receive a reentrant event.");
                if (terminal) return result("Terminal", state, null, "Reject");
                sending = true;
                try {
                    switch (state) {
                        case "Staging": {
                            let nextBoard = board;
                            const fromState = state;
                            board = nextBoard;
                            state = "Rejected";
                            revision += 1;
                            terminal = true;
                            return result("Failed", fromState, state, "Reject", "delivery rejected");
                        }
                        case "Retrying": {
                            let nextBoard = board;
                            const fromState = state;
                            board = nextBoard;
                            state = "Rejected";
                            revision += 1;
                            terminal = true;
                            return result("Failed", fromState, state, "Reject", "delivery rejected");
                        }
                        case "Completing": {
                            let nextBoard = board;
                            const fromState = state;
                            board = nextBoard;
                            state = "Rejected";
                            revision += 1;
                            terminal = true;
                            return result("Failed", fromState, state, "Reject", "delivery rejected");
                        }
                        default: return result("Unhandled", state, null, "Reject");
                    }
                } finally {
                    sending = false;
                }
            },
            sendReset() {
                if (sending) throw new Error("A Copeland flow session cannot receive a reentrant event.");
                if (terminal) return result("Terminal", state, null, "Reset");
                sending = true;
                try {
                    switch (state) {
                        case "Accepted": {
                            let nextBoard = board;
                            nextBoard = { ...nextBoard, total: 0 };
                            board = nextBoard;
                            nextBoard = { ...nextBoard, attempts: 0 };
                            board = nextBoard;
                            nextBoard = { ...nextBoard, accepted: false };
                            board = nextBoard;
                            const fromState = state;
                            board = nextBoard;
                            state = "Idle";
                            revision += 1;
                            return result("Transitioned", fromState, state, "Reset");
                        }
                        default: return result("Unhandled", state, null, "Reset");
                    }
                } finally {
                    sending = false;
                }
            },
            sendCancel() {
                if (sending) throw new Error("A Copeland flow session cannot receive a reentrant event.");
                if (terminal) return result("Terminal", state, null, "Cancel");
                sending = true;
                try {
                    switch (state) {
                        case "Idle": {
                            let nextBoard = board;
                            const fromState = state;
                            board = nextBoard;
                            state = "Cancelled";
                            revision += 1;
                            terminal = true;
                            return result("Failed", fromState, state, "Cancel", "delivery cancelled");
                        }
                        case "Retrying": {
                            let nextBoard = board;
                            const fromState = state;
                            board = nextBoard;
                            state = "Cancelled";
                            revision += 1;
                            terminal = true;
                            return result("Failed", fromState, state, "Cancel", "delivery cancelled");
                        }
                        default: return result("Unhandled", state, null, "Cancel");
                    }
                } finally {
                    sending = false;
                }
            },
            sendTick(amount) {
                if (sending) throw new Error("A Copeland flow session cannot receive a reentrant event.");
                if (terminal) return result("Terminal", state, null, "Tick");
                sending = true;
                try {
                    switch (state) {
                        case "Staging": {
                            if (!((amount > 0))) return result("Unhandled", state, null, "Tick");
                            let nextBoard = board;
                            nextBoard = { ...nextBoard, total: (board["total"] + amount) };
                            board = nextBoard;
                            nextBoard = { ...nextBoard, sequence: nextSequence(board["sequence"]) };
                            board = nextBoard;
                            const fromState = state;
                            board = nextBoard;
                            state = "Staging";
                            revision += 1;
                            return result("Transitioned", fromState, state, "Tick");
                        }
                        case "Retrying": {
                            if (!((amount > 0))) return result("Unhandled", state, null, "Tick");
                            let nextBoard = board;
                            nextBoard = { ...nextBoard, total: (board["total"] + amount) };
                            board = nextBoard;
                            const fromState = state;
                            board = nextBoard;
                            state = "Retrying";
                            revision += 1;
                            return result("Transitioned", fromState, state, "Tick");
                        }
                        case "Accepted": {
                            if (!((amount > 0))) return result("Unhandled", state, null, "Tick");
                            let nextBoard = board;
                            nextBoard = { ...nextBoard, total: (board["total"] + amount) };
                            board = nextBoard;
                            const fromState = state;
                            board = nextBoard;
                            state = "Completing";
                            revision += 1;
                            return result("Transitioned", fromState, state, "Tick");
                        }
                        default: return result("Unhandled", state, null, "Tick");
                    }
                } finally {
                    sending = false;
                }
            },
        };
        return Object.freeze(session);
    },
});
