function increment(value: number): number { return value + 1; }
function double(value: number): number { return value * 2; }

function main(): number {
    const basic: number = 20 |> increment;
    return basic |> double |> increment;
}
