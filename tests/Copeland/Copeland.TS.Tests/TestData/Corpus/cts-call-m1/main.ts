type Operation = (value: number) => number;

record Box { operation: Operation; }
enum Choice { Value(operation: Operation), }
enum Failure { Bad, }

function increment(value: number): number { return value + 1; }
function identity<T>(value: T): T { return value; }

function apply(operation: Operation, value: number): number {
    return operation(value);
}

function makeAdder(base: number): Operation {
    return capture { base } (value: number) => base + value;
}

function main(): number {
    const named: Operation = increment;
    const closed = identity<number>;
    const double: Operation = value => value * 2;
    const block: Operation = (value: number): number => {
        const adjusted = value + 1;
        return adjusted * 2;
    };
    const escaped = makeAdder(10);
    const stored: Operation[] = [named, closed, double, block, escaped];
    const box: Box = { operation: escaped };
    const choice: Choice = Choice.Value(box.operation);
    const result: Operation ! Failure = ok(match choice { Value(operation) => operation, });
    return match result { ok(operation) => apply(operation, 1), err(error) => 0, };
}
