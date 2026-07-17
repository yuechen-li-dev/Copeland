type Operation = (value: number) => number;

function increment(value: number): number {
    return value + 1;
}

function identity<T>(value: T): T {
    return value;
}

function apply(operation: Operation, value: number): number {
    return operation(value);
}

function provide(): Operation {
    return increment;
}

function main(): number {
    const first = increment;
    const second = identity<number>;
    const supplied = provide();
    return apply(first, 20) + second(supplied(20));
}
