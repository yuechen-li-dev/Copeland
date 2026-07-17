type Operation = (value: number) => number;
function action(value: number): number { return value + 100; }
function increment(value: number): number { return value + 1; }
function main(): number { const action: Operation = increment; return action(1); }
