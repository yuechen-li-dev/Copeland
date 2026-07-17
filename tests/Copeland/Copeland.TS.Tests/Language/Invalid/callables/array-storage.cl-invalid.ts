function increment(value: number): number { return value + 1; }
function main(): number { const values: ((value: number) => number)[] = [increment]; return 0; }
