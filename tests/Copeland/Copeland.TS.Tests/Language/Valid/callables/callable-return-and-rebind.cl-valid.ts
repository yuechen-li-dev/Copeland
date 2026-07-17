type Operation = (value: number) => number;
function increment(value: number): number { return value + 1; }
function decrement(value: number): number { return value - 1; }
function provide(): Operation { return increment; }
function main(): number { let operation: Operation = provide(); operation = decrement; return operation(2); }
