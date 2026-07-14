record table Samples {
    x: [1];
}

function invalid(): Samples.Row {
    return { x: 1 };
}
