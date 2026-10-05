function fib(n: int): int {
    if (n < 2) {
        return n;
    }
    return fib(n - 1) + fib(n - 2);
}

export function runFib(bias: int): int {
    let sum: int = 0;
    for (let i: int = 0; i < 5; i = i + 1) {
        sum = sum + fib(32 + i % 2 + bias);
    }
    return sum;
}
