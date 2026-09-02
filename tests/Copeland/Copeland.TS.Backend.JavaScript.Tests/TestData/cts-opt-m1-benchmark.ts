record table Bench {
    value: int = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
    weight: number = [0.5, 1.5, 2.5, 3.5, 4.5, 5.5, 6.5, 7.5, 8.5, 9.5];
}

function rowAccess(iterations: int): int {
    let index: int = 0;
    let total: int = 0;
    while (index < iterations) {
        const row: Bench.Row = Bench[index % 10]!;
        total = total + row.value;
        index = index + 1;
    }
    return total;
}

function columnAccess(iterations: int): int {
    let index: int = 0;
    let total: int = 0;
    while (index < iterations) {
        const values: column int = Bench.value;
        total = total + values[0]!;
        index = index + 1;
    }
    return total;
}

function cellAccess(iterations: int): int {
    let index: int = 0;
    let total: int = 0;
    while (index < iterations) {
        total = total + Bench.value[index % 10]!;
        index = index + 1;
    }
    return total;
}

function queryAccess(iterations: int): int {
    let index: int = 0;
    let total: int = 0;
    while (index < iterations) {
        total = total + Bench.value.sum();
        index = index + 1;
    }
    return total;
}

function main(): int {
    return rowAccess(100) + columnAccess(100) + cellAccess(100) + queryAccess(100);
}
