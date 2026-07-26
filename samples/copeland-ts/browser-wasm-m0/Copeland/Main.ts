import { ApplyIncrement, ApplyReset, CounterEvent, CounterState, Reduce } from "./Counter";
import { RunWorkload } from "./Workload";

export function ApplyEvent(count: int, event: CounterEvent): int {
    const state: CounterState = { count: count };
    const next: CounterState = Reduce(state, event);
    return next.count;
}

export function ApplyIncrementEvent(count: int): int {
    return ApplyEvent(count, CounterEvent.Increment);
}

export function ApplyResetEvent(count: int): int {
    return ApplyEvent(count, CounterEvent.Reset);
}

export function Workload(iterations: int): int {
    return RunWorkload(iterations);
}
