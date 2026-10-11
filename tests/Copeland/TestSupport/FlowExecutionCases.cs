namespace Copeland.TS.Tests.TestSupport;

internal static class FlowExecutionCases
{
    public static IEnumerable<object[]> All()
    {
        yield return [Nested, 22];
        yield return [Failure, 108];
        yield return [Overflow, 12];
        yield return [Transition, 7];
        yield return [VoidChild, 7];
        yield return [InvalidDepth, 2];
    }

    private const string Nested = """
        flow Leaf(count: int) -> int {
            board { remaining: int = count; }
            state Work initial {
                if (board.remaining == 0) { finish count; }
                board.remaining = board.remaining - 1;
                yield;
            }
        }
        flow Middle(count: int) -> int {
            board { seen: int = 0; }
            state Begin initial { push Leaf(count) -> Accept; }
            state Accept(value: int) {
                if (board.seen == 0) { board.seen = 1; yield; }
                finish value + 10;
            }
        }
        flow Root(count: int) -> int {
            stack 3;
            state Begin initial { push Middle(count) -> Accept; }
            state Accept(value: int) { finish value + 10; }
        }
        function main(): int {
            const initial = Root.start(2);
            let frame = initial;
            let step: int = 0;
            while (step < 12) { frame = Root.step(frame); step = step + 1; }
            return match frame.outcome {
                case RootOutcome.Active => 100,
                case RootOutcome.Completed(value) => value + initial.depth - 1,
                case RootOutcome.Fault(code) => 200 + code,
            };
        }
        """;

    private const string Failure = """
        flow Leaf(input: int) -> int ! int {
            state Work initial { fail input; }
        }
        flow Middle(input: int) -> int ! int {
            state Begin initial { push Leaf(input) -> Accept ! Reject; }
            state Accept(value: int) { finish value; }
            state Reject(error: int) { fail error + 1; }
        }
        flow Root(input: int) -> int {
            state Begin initial { push Middle(input) -> Accept ! Reject; }
            state Accept(value: int) { finish value; }
            state Reject(error: int) { finish error + 100; }
        }
        function main(): int {
            let frame = Root.start(7);
            let step: int = 0;
            while (step < 8) { frame = Root.step(frame); step = step + 1; }
            return match frame.outcome {
                case RootOutcome.Active => 1000,
                case RootOutcome.Completed(value) => value,
                case RootOutcome.Fault(code) => 2000 + code,
            };
        }
        """;

    private const string Overflow = """
        flow Recursive(count: int) -> int {
            stack 2;
            state Begin initial { push Recursive(count + 1) -> Accept; }
            state Accept(value: int) { finish value; }
        }
        function main(): int {
            const initial = Recursive.start(0);
            const pushed = Recursive.step(initial);
            const overflow = Recursive.step(pushed);
            const unchanged = Recursive.step(overflow);
            return match unchanged.outcome {
                case RecursiveOutcome.Active => 100,
                case RecursiveOutcome.Completed(value) => 200,
                case RecursiveOutcome.Fault(code) => code * 10 + pushed.depth + initial.depth - 1,
            };
        }
        """;

    private const string Transition = """
        flow Once(input: int) -> int {
            board { count: int = 0; }
            state Begin initial { board.count = 1; transition Done; }
            state Done { finish input + board.count; }
        }
        function main(): int {
            const started = Once.start(6);
            const transitioned = Once.step(started);
            const completed = Once.step(transitioned);
            return match completed.outcome {
                case OnceOutcome.Active => 100,
                case OnceOutcome.Yielded => 200,
                case OnceOutcome.Completed(value) => value,
            };
        }
        """;

    private const string VoidChild = """
        flow Done() {
            state End initial { finish; }
        }
        flow Parent() -> int {
            state Begin initial { push Done() -> Accept; }
            state Accept { finish 7; }
        }
        function main(): int {
            let frame = Parent.start();
            frame = Parent.step(frame);
            frame = Parent.step(frame);
            frame = Parent.step(frame);
            return match frame.outcome {
                case ParentOutcome.Active => 100,
                case ParentOutcome.Completed(value) => value,
                case ParentOutcome.Fault(code) => 200,
            };
        }
        """;

    private const string InvalidDepth = """
        flow Child(input: int) -> int {
            state Work initial { finish input; }
        }
        flow Parent(input: int) -> int {
            stack 2;
            state Begin initial { push Child(input) -> Accept; }
            state Accept(value: int) { finish value; }
        }
        function main(): int {
            const started = Parent.start(7);
            const invalid = started with { depth: 99 };
            const rejected = Parent.step(invalid);
            return match rejected.outcome {
                case ParentOutcome.Active => 100,
                case ParentOutcome.Completed(value) => 200,
                case ParentOutcome.Fault(code) => code,
            };
        }
        """;
}
