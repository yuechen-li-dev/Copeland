// expect: COPE-BATCH-0014
type Operation = (value: number) => number;
function main(values: number[], operation: Operation): number[] {
    return batch values as value {
        return value |> operation;
    };
}
