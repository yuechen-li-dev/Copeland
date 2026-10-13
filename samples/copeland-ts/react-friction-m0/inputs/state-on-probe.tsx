import { createElement } from "react";

record Model { count: int; }

export function Counter(seed: int, label: string): ReactNode {
    state: Model = { count: seed };
    on Add(value: int) => state with { count: state.count + value };
    on Restore(value: int) => state with { count: value };
    return <section aria-label={label}>
        <span>{state.count}</span>
        <button onClick={capture { send: on.Add } () => send(1)}>Add</button>
        <button onClick={capture { state, send: on.Restore } () => send(state.count)}>Snapshot</button>
    </section>;
}

export function Fire(send: (value: int) => void, value: int): void {
    send(value);
}

export function ReadOnly(seed: int): ReactNode {
    state: int = seed;
    return <section aria-label="Read only"><span>{state}</span></section>;
}

export function List(): ReactNode {
    state: int = 0;
    on Set(values: int[]) => values[0];
    return <section aria-label="List"><span>{state}</span></section>;
}

flow Counting(limit: int) -> int {
    board { count: int = 0; }
    state Work initial {
        if (board.count == limit) { finish board.count; }
        board.count = board.count + 1;
        yield;
    }
}

export function Machine(limit: int): ReactNode {
    state: CountingFrame = Counting.start(limit);
    on Tick() => Counting.step(state);
    return <section aria-label="Machine">
        <span>{state.board.count}</span>
        <button onClick={on.Tick}>Tick</button>
    </section>;
}

function CheckedView(value: int): ReactNode {
    const one: int[] = [7];
    return <section aria-label="Checked"><span>{one[value]}</span><span>{value}</span></section>;
}

export function Checked(): ReactNode {
    state: int = 0;
    on Set(value: int) => value;
    return CheckedView(state);
}

flow Child(value: int) -> int ! int {
    state Work initial {
        if (value < 0) { fail value; }
        finish value;
    }
}

flow Parent(value: int) -> int {
    stack 2;
    state Begin initial { push Child(value) -> Accept ! Reject; }
    state Accept(result: int) { finish result + 10; }
    state Reject(error: int) { finish error; }
}

export function StackMachine(value: int, label: string): ReactNode {
    state: ParentFrame = Parent.start(value);
    on Tick() => Parent.step(state);
    const outcome: int = match state.outcome {
        case ParentOutcome.Active => 100,
        case ParentOutcome.Completed(result) => result,
        case ParentOutcome.Fault(code) => 200 + code,
    };
    return <section aria-label={label}><span>{state.depth}</span><span>{outcome}</span><button onClick={on.Tick}>Tick</button></section>;
}

flow Recursive(value: int) -> int {
    stack 2;
    state Begin initial { push Recursive(value + 1) -> Accept; }
    state Accept(result: int) { finish result; }
}

export function OverflowMachine(): ReactNode {
    state: RecursiveFrame = Recursive.start(0);
    on Tick() => Recursive.step(state);
    const outcome: int = match state.outcome {
        case RecursiveOutcome.Active => 100,
        case RecursiveOutcome.Completed(result) => result,
        case RecursiveOutcome.Fault(code) => code,
    };
    return <section aria-label="Overflow"><span>{state.depth}</span><span>{outcome}</span><button onClick={on.Tick}>Tick</button></section>;
}
