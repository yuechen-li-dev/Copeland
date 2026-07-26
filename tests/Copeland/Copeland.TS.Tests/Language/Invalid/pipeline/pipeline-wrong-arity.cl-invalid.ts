// expect: COPE-TYPE-0004
function add(left: number, right: number): number { return left + right; }
function main(): number { return 1 |> add; }
