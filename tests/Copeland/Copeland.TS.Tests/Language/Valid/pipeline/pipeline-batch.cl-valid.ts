function normalize(value: number): number { return value * 2; }

function main(values: number[]): number[] {
    return batch values as value {
        return value |> normalize;
    };
}
