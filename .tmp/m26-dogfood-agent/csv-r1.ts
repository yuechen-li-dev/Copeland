function parseCsvIntegers(text: string): int[] {
    return String.Split(text, ",").map(part => parseInt(part.trim(), 10));
}
