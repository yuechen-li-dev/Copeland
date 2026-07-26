record table Samples {
    x: number = [1, 2, 3];
    label: string = ["a", "b", "c"];
}

function row(index: number): Samples.Row ! TableBoundsError {
    return Samples[index];
}

function value(index: number): number ! TableBoundsError {
    return Samples.x[index];
}
