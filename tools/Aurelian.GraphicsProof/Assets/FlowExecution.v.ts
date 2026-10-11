flow Child(count: u32) -> u32 ! u32 {
    board { remaining: u32 = count; }
    state Work initial {
        if (count == 4) {
            fail count;
        }
        if (board.remaining == 0) {
            finish count;
        }
        board.remaining = board.remaining - 1;
        yield;
    }
}
flow Middle(count: u32) -> u32 ! u32 {
    state Begin initial { run Child(count) -> Accept ! Reject; }
    state Accept(value: u32) { finish value + 10; }
    state Reject(error: u32) { fail error; }
}
flow Root(count: u32) -> u32 {
    state Begin initial { run Middle(count) -> Accept ! Reject; }
    state Accept(value: u32) { finish value + 10; }
    state Reject(error: u32) { finish error + 100; }
}
flow Single(count: u32) -> u32 {
    state Begin initial { run Child(count) -> Accept ! Reject; }
    state Accept(value: u32) { finish value + 10; }
    state Reject(error: u32) { finish error + 100; }
}
function Resolve(frame: SingleFrame): u32 {
    return match frame.outcome {
        case SingleOutcome.Active => 1000,
        case SingleOutcome.Completed(payload) => payload.value,
        case SingleOutcome.Fault(payload) => payload.code + 2000,
    };
}
export function Run(count: u32): u32 {
    var frame = Single.start(count);
    static for (let tick of [0, 1, 2, 3, 4]) {
        frame = Single.step(frame);
    }
    return Resolve(frame);
}
export function Nested(count: u32): u32 {
    var frame = Root.start(count);
    static for (let tick of [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]) {
        frame = Root.step(frame);
    }
    return match frame.outcome {
        case RootOutcome.Active => 1000,
        case RootOutcome.Completed(payload) => payload.value,
        case RootOutcome.Fault(payload) => payload.code + 2000,
    };
}
export function Replay(): u32 {
    const initial = Root.start(2);
    const checkpoint = Root.step(initial);
    const forward = Root.step(checkpoint);
    const replay = Root.step(checkpoint);
    // Inspect flat state positions; replaying a checkpoint must select the same state.
    const result: u32 = Root.position(forward) * 10 + Root.position(replay) + Root.position(initial);
    return result;
}

flow Repeat(count: u32) -> u32 {
    board { total: u32 = 0; rounds: u32 = 0; }
    state Begin initial { run Child(count) -> Accept ! Reject; }
    state Accept(value: u32) {
        board.total = board.total + value;
        board.rounds = board.rounds + 1;
        transition Decide;
    }
    state Reject(error: u32) { finish error + 100; }
    state Decide {
        if (board.rounds < 2) {
            transition Begin;
        }
        finish board.total;
    }
}
export function Reenter(count: u32): u32 {
    var frame = Repeat.start(count);
    var yields: u32 = 0;
    static for (let tick of [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]) {
        frame = Repeat.step(frame);
        yields = yields + match frame.stepKind {
            case RepeatStepKind.Started => 0,
            case RepeatStepKind.Transitioned => 0,
            case RepeatStepKind.Yielded => 1,
            case RepeatStepKind.Entered => 0,
            case RepeatStepKind.Completed => 0,
            case RepeatStepKind.Failed => 0,
            case RepeatStepKind.Fault => 0,
        };
    }
    return match frame.outcome {
        case RepeatOutcome.Active => 1000,
        case RepeatOutcome.Completed(payload) => payload.value * 100 + yields,
        case RepeatOutcome.Fault(payload) => payload.code + 2000,
    };
}

record Receipt { value: u32; }
flow RecordChild(count: u32) -> Receipt {
    state Work initial { finish { value: count + 1 }; }
}
flow RecordParent(count: u32) -> u32 {
    state Begin initial { run RecordChild(count) -> First; }
    state First(receipt: Receipt) { run RecordChild(receipt.value) -> Last; }
    state Last(receipt: Receipt) { finish receipt.value; }
}
export function Reuse(count: u32): u32 {
    var frame = RecordParent.start(count);
    static for (let tick of [0, 1, 2, 3, 4]) {
        frame = RecordParent.step(frame);
    }
    return match frame.outcome {
        case RecordParentOutcome.Active => 1000,
        case RecordParentOutcome.Completed(payload) => payload.value,
        case RecordParentOutcome.Fault(payload) => payload.code + 2000,
    };
}

function ResolveRoot(frame: RootFrame): u32 {
    return match frame.outcome {
        case RootOutcome.Active => 1000,
        case RootOutcome.Completed(payload) => payload.value,
        case RootOutcome.Fault(payload) => payload.code + 2000,
    };
}
export function ReplayCompletion(count: u32): u32 {
    // Retain a snapshot while the grandchild owns control.
    const entered = Root.step(Root.step(Root.start(count)));
    const checkpoint = Root.step(entered);
    var forward = checkpoint;
    var replay = checkpoint;
    static for (let tick of [0, 1, 2, 3, 4, 5, 6, 7]) {
        forward = Root.step(forward);
        replay = Root.step(replay);
    }
    var expectedPosition: u32 = 6;
    if (count == 0) {
        expectedPosition = 4;
    }
    if (count == 4) {
        expectedPosition = 5;
    }
    if (Root.position(checkpoint) != expectedPosition) {
        return 0;
    }
    return ResolveRoot(forward) * 10000 + ResolveRoot(replay);
}
