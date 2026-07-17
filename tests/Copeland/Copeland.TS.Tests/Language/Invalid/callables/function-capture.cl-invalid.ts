function increment(value: number): number { return value + 1; }
function main(): number {
    const operation = capture { increment } (value: number) => value;
    return operation(1);
}
