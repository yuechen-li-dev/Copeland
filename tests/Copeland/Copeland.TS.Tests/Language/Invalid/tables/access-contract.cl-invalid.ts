record table First {
    x: [1];
}

record table Second {
    x: [2];
}

function expectFirst(value: First.Row): void {
    return;
}

function invalid(text: string): void {
    const values: number[] = [1];
    values[0];
    First.missing;
    First[text];
    First.x == First.x;
    const row: First.Row = { x: 1 };
    expectFirst(Second[0]!);
    return;
}
