import { CounterEvent, CounterState, Reduce, SendIncrement, SendReset } from "./Counter";
import { dispatch, onClick, setText } from "@copeland/browser-v1";

export function Main(): void {
    const send: (event: CounterEvent) => void = dispatch<CounterState, CounterEvent>(
        { count: 0 },
        Reduce,
        state => setText("count", `Count: ${state.count}`));

    onClick("increment", capture { send } () => SendIncrement(send));
    onClick("reset", capture { send } () => SendReset(send));
}
