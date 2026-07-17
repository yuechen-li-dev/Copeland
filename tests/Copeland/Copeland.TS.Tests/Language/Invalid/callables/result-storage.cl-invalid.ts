enum Failure { Bad, }
function increment(value: number): number { return value + 1; }
function main(): number { const result: ((value: number) => number) ! Failure = ok(increment); return 0; }
