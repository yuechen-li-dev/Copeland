function value(): number {
    return 1;
}

record table Invalid {
    x: [value()];
}
