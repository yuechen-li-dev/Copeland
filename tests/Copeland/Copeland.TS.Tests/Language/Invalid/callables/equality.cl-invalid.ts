function increment(value: number): number { return value + 1; }
function main(): boolean { const operation = increment; return operation == increment; }
