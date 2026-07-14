function nested(): void {
    record table Inner {
        x: [1];
    }
}

function missing(value: Unknown.Row): column Unknown {
    return value;
}
