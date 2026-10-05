export function runSieve(limit: int): int {
    let count: int = 0;
    for (let rep: int = 0; rep < 5; rep = rep + 1) {
        const composite: MutableArray<boolean> = MutableArray<boolean>(limit + 1);
        count = 0;
        for (let i: int = 2; i <= limit; i = i + 1) {
            if (!composite[i]) {
                count = count + 1;
                if (i <= limit / i) {
                    for (let j: int = i * i; j <= limit; j = j + i) {
                        composite[j] = true;
                    }
                }
            }
        }
    }
    return count;
}
