function main(): number {
    const value: number = 1;
    const operation = capture { value, value } (input: number) => value + input;
    return operation(1);
}
