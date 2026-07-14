record table Values {
    x: [1];
}

function invalid(index: string): void {
    Values = Values;
    Values.x = Values.x;
    const row: Values.Row = { x: 1 };
    Values.x[0] = 2;
    const result: Values.Row ! TableBoundsError = Values[index];
    return;
}
