record table Samples {
    x: [1];
}

function invalid(): boolean {
    return Samples.x == Samples.x;
}
