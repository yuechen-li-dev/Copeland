// expect: COPE-TYPE-0005
function double(value: number): number { return value * 2; }
function main(): number { return "one" |> double; }
