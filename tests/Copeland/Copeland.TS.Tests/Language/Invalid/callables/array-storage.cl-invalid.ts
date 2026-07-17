function increment(value: number): number { return value + 1; }
record table Invalid { operation: ((value: number) => number) = [increment]; }
