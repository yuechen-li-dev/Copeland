record Input {
    category: int;
    amount: int;
}

record Output {
    category: int;
    amount: int;
}

function Run(iterations: int): int {
    const input: Input[] = [
        { category: 0, amount: 3 }, { category: 1, amount: 7 },
        { category: 2, amount: 11 }, { category: 3, amount: 13 },
        { category: 4, amount: 17 }, { category: 5, amount: 19 },
        { category: 6, amount: 23 }, { category: 7, amount: 29 },
        { category: 0, amount: 31 }, { category: 1, amount: 37 },
        { category: 2, amount: 41 }, { category: 3, amount: 43 },
        { category: 4, amount: 47 }, { category: 5, amount: 53 },
        { category: 6, amount: 59 }, { category: 7, amount: 61 }
    ];
    let checksum: int = 0;

    for (let index: int = 0; index < iterations; index = index + 1) {
        const item: Input = input[index % input.length];
        const mapped: Output = { category: item.category, amount: (item.amount * 3) + index };
        if ((mapped.category % 2) == 0) {
            checksum = (checksum + mapped.amount) % 1000003;
        }
        else {
            checksum = (checksum - mapped.category) % 1000003;
        }
    }

    return checksum;
}
