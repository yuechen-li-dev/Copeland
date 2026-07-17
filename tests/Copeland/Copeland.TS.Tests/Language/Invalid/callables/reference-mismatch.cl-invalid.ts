type Flag = () => boolean;
function increment(value: number): number { return value + 1; }
function main(): number { const check: Flag = increment; return 0; }
