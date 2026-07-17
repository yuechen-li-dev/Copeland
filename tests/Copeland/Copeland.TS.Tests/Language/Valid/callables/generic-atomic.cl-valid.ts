type Operation = (value: number) => number;
function identity<T>(value: T): T { return value; }
function increment(value: number): number { return value + 1; }
function main(): number { const operation: Operation = identity(increment); return operation(1); }
