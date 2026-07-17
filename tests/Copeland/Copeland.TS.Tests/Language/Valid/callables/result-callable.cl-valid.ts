type Operation = (value: number) => number;
enum Failure { Bad, }

function makeAdder(base: number): Operation {
    return capture { base } (value: number) => base + value;
}

function main(): number {
    const result: Operation ! Failure = ok(makeAdder(1));
    return match result { ok(operation) => operation(41), err(error) => 0, };
}
