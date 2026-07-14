function main(): number {
    let value: number = 0;

    for (;;) {
        value = value + 1;
        if (value == 3) {
            break;
        }
    }

    for (; value < 5;) {
        value = value + 1;
    }

    return value;
}
