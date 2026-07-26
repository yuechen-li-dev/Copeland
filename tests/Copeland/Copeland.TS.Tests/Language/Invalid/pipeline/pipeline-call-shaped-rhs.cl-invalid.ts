// expect: COPE-PIPE-0001
function add(value: number, amount: number): number { return value + amount; }
function main(): number { return 1 |> add(2); }
