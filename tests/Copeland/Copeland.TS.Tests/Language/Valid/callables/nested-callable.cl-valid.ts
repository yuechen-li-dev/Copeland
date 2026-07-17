type Operation = (value: number) => number;
function increment(value: number): number { return value + 1; }
function provide(operation: Operation): Operation { return operation; }
function main(): number { return provide(increment)(1); }
