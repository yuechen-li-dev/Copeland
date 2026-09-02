"use strict";

function __cope_m3_panic_0() {
    throw new Error("Copeland JavaScript backend invariant failure.");
}

function __cope_m3_make_1(type, tag, payload) {
    const value = Object.freeze(Object.assign(Object.create(null), { $type: type, $tag: tag, $payload: Object.freeze(payload) }));
    return value;
}

const __cope_m3_record_type_r1_2 = Symbol("r1");

function __cope_m3_record_make_r1_4(field0, field1) {
    return { [__cope_m3_record_type_r1_2]: __cope_m3_record_type_r1_2, $f0: field0, $f1: field1 };
}

function __cope_m3_record_require_r1_5(value) {
    if (typeof value !== "object" || value === null || value[__cope_m3_record_type_r1_2] !== __cope_m3_record_type_r1_2 || Object.keys(value).length !== 2 || Object.getOwnPropertySymbols(value).length !== 1 || !Object.prototype.hasOwnProperty.call(value, "$f0") || !Object.prototype.hasOwnProperty.call(value, "$f1")) {
        __cope_m3_panic_0();
    }
    if (!((__cope_m3_validate_18(value.$f0), true))) { __cope_m3_panic_0(); }
    if (!(typeof value.$f1 === "number")) { __cope_m3_panic_0(); }
}

const __cope_m3_record_type_r2_8 = Symbol("r2");

function __cope_m3_record_make_r2_10(field0, field1, field2, field3) {
    return { [__cope_m3_record_type_r2_8]: __cope_m3_record_type_r2_8, $f0: field0, $f1: field1, $f2: field2, $f3: field3 };
}

function __cope_m3_record_require_r2_11(value) {
    if (typeof value !== "object" || value === null || value[__cope_m3_record_type_r2_8] !== __cope_m3_record_type_r2_8 || Object.keys(value).length !== 4 || Object.getOwnPropertySymbols(value).length !== 1 || !Object.prototype.hasOwnProperty.call(value, "$f0") || !Object.prototype.hasOwnProperty.call(value, "$f1") || !Object.prototype.hasOwnProperty.call(value, "$f2") || !Object.prototype.hasOwnProperty.call(value, "$f3")) {
        __cope_m3_panic_0();
    }
    if (!(typeof value.$f0 === "number")) { __cope_m3_panic_0(); }
    if (!(typeof value.$f1 === "number")) { __cope_m3_panic_0(); }
    if (!(typeof value.$f2 === "number")) { __cope_m3_panic_0(); }
    if (!(typeof value.$f3 === "boolean")) { __cope_m3_panic_0(); }
}

const __cope_m3_type_16 = Symbol("AuditKind");
const __cope_m3_type_16_case_0 = Object.freeze({ $type: __cope_m3_type_16, $tag: "Started" });

function __cope_m3_validate_18(value) {
    if (typeof value !== "object" || value === null || value.$type !== __cope_m3_type_16 || typeof value.$tag !== "string") {
        __cope_m3_panic_0();
    }
    switch (value.$tag) {
        case "Started":
            if (Object.keys(value).length !== 2 || Object.getOwnPropertySymbols(value).length !== 0) {
                __cope_m3_panic_0();
            }
            return;
        case "Retried":
            if (Object.keys(value).length !== 3 || Object.getOwnPropertySymbols(value).length !== 0) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value, "$p0") || !(typeof value.$p0 === "number")) {
                __cope_m3_panic_0();
            }
            return;
        case "Completed":
            if (Object.keys(value).length !== 3 || Object.getOwnPropertySymbols(value).length !== 0) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value, "$p0") || !(typeof value.$p0 === "number")) {
                __cope_m3_panic_0();
            }
            return;
        case "Failed":
            if (Object.keys(value).length !== 3 || Object.getOwnPropertySymbols(value).length !== 0) {
                __cope_m3_panic_0();
            }
            if (!Object.prototype.hasOwnProperty.call(value, "$p0") || !(typeof value.$p0 === "string")) {
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
    const __cope_m3_match_20 = __cope_m3_record_receiver_19.$f0;
    let __cope_m3_match_value_21;
    switch (__cope_m3_match_20.$tag) {
        case "Started":
        {
            const __cope_m3_record_receiver_22 = entry;
            __cope_m3_match_value_21 = __cope_m3_record_receiver_22.$f1;
            break;
        }
        case "Retried":
        {
            const count = __cope_m3_match_20.$p0;
            const __cope_m3_record_receiver_23 = entry;
            __cope_m3_match_value_21 = (__cope_m3_record_receiver_23.$f1 + count);
            break;
        }
        case "Completed":
        {
            const total = __cope_m3_match_20.$p0;
            const __cope_m3_record_receiver_24 = entry;
            __cope_m3_match_value_21 = (__cope_m3_record_receiver_24.$f1 + total);
            break;
        }
        case "Failed":
        {
            const reason = __cope_m3_match_20.$p0;
            const __cope_m3_record_receiver_25 = entry;
            __cope_m3_match_value_21 = (__cope_m3_record_receiver_25.$f1 + reason.length);
            break;
        }
        default:
            __cope_m3_panic_0();
    }
    return __cope_m3_match_value_21;
}

function main() {
    const __cope_m3_record_init_26 = { $type: __cope_m3_type_16, $tag: "Completed", $p0: 42 };
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
