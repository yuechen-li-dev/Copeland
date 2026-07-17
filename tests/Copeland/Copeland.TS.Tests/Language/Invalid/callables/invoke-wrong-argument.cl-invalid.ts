type Operation = (value: number) => number;
function increment(value: number): number { return value + 1; }
function main(): number { const operation: Operation = increment; return operation("bad"); }
