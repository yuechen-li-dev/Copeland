type Operation = (value: number) => number;
record Box { operation: Operation; }
enum Choice { Value(operation: Operation), }

function makeAdder(base: number): Operation {
    return capture { base } (value: number) => base + value;
}

function main(): number {
    const values: Operation[] = [makeAdder(1)];
    const box: Box = { operation: makeAdder(2) };
    const choice: Choice = Choice.Value(box.operation);
    return match choice { Value(operation) => operation(40), };
}
