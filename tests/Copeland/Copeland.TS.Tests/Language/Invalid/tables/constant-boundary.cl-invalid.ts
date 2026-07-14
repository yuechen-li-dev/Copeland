function value(): number {
    return 1;
}

const named: number = 2;

record table Invalid {
    call: [value()];
    variable: [named];
}
