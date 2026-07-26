type SamplesAlias = Samples;
type SampleRow = SamplesAlias.Row;
type NumericColumn = column number;

record table Samples {
    value: number = [1, 2];
}

function read(row: SampleRow): number {
    return row.value;
}

function first(values: NumericColumn): number ! TableBoundsError {
    return values[0];
}
