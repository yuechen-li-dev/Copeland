function main(): number {
    let base: number = 1;
    const operation = capture { base } (value: number) => {
        base = value;
        return base;
    };
    return operation(2);
}
