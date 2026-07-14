record table Duplicate {
    x: [1];
    x: [2];
}

record table Ragged {
    x: [1, 2];
    y: [1];
}

record table Empty {
    value: [];
}

function annotations(value: Duplicate.Row): column Missing {
    return value;
}
