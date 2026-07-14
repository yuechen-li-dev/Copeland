record table Empty {
}

record table DuplicateTable {
    x: [1];
}

record table DuplicateTable {
    y: [2];
}

record table Columns {
    duplicate: [1];
    duplicate: [2];
    mixed: [1, "two"];
    mismatch: number = ["wrong"];
}

record Node {
    next: Node;
}

record table Recursive {
    node: Node = [];
}
