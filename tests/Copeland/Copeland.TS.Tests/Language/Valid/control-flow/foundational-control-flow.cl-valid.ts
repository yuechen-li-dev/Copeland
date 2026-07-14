function main(): number {
    let total: number = 0;

    for (let index: number = 0; index < 8; index = index + 1) {
        if (index == 2) {
            continue;
        }

        total = total + index;
        if (total > 12) {
            break;
        }
    }

    while (total < 20) {
        total = total + 1;
    }

    return total;
}
