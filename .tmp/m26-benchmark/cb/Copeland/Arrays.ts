record Lcg {
    seed: float;
}

function nextSeed(seed: float): float {
    return (seed * 1103515245.0 + 12345.0) % 2147483648.0;
}

function quickSort(values: MutableArray<int>, low: int, high: int): void {
    let lo: int = low;
    let hi: int = high;
    while (lo < hi) {
        const pivot: int = values[lo + (hi - lo) / 2];
        let i: int = lo;
        let j: int = hi;
        while (i <= j) {
            while (values[i] < pivot) {
                i = i + 1;
            }
            while (values[j] > pivot) {
                j = j - 1;
            }
            if (i <= j) {
                const swap: int = values[i];
                values[i] = values[j];
                values[j] = swap;
                i = i + 1;
                j = j - 1;
            }
        }
        if (j - lo < hi - i) {
            quickSort(values, lo, j);
            lo = i;
        } else {
            quickSort(values, i, hi);
            hi = j;
        }
    }
}

export function runArrays(rounds: int, count: int): float {
    let seed: float = 42.0;
    let acc: float = 0.0;
    for (let round: int = 0; round < rounds; round = round + 1) {
        const xs: MutableArray<int> = MutableArray<int>(count);
        for (let i: int = 0; i < count; i = i + 1) {
            seed = nextSeed(seed);
            xs[i] = Int.Truncate(seed % 100000.0);
        }
        quickSort(xs, 0, count - 1);
        let kept: int = 0;
        for (let i: int = 0; i < count; i = i + 1) {
            if ((xs[i] * 2) % 3 == 0) {
                kept = kept + 1;
            }
        }
        const ys: MutableArray<int> = MutableArray<int>(kept);
        let k: int = 0;
        for (let i: int = 0; i < count; i = i + 1) {
            const doubled: int = xs[i] * 2;
            if (doubled % 3 == 0) {
                ys[k] = doubled;
                k = k + 1;
            }
        }
        let sum: float = 0.0;
        for (const y of ys) {
            sum = sum + Float.From(y);
        }
        acc = acc + sum + Float.From(xs[count / 2]);
    }
    return acc;
}
