function Run(iterations: int): int {
    let value: int = 17;

    for (let index: int = 0; index < iterations; index = index + 1) {
        value = ((value * 31) + index) % 1000003;
        if ((value % 17) == 0) {
            value = value + 19;
        }
    }

    return value;
}
