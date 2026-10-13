// Ordinary immutable collection operations. MutableArray is local construction
// storage; each result is a frozen snapshot and never aliases that storage.
export function Append<T>(items: T[], item: T): T[] {
    const result: MutableArray<T> = MutableArray<T>(items.length + 1, item);
    for (let index: int = 0; index < items.length; index = index + 1) {
        result[index] = items[index];
    }
    return result.freeze();
}

export function Filter<T>(items: T[], predicate: (item: T) => boolean): T[] {
    if (items.length == 0) {
        return [];
    }
    const included: MutableArray<boolean> = MutableArray<boolean>(items.length);
    let count: int = 0;
    for (let index: int = 0; index < items.length; index = index + 1) {
        included[index] = predicate(items[index]);
        if (included[index]) {
            count = count + 1;
        }
    }
    const result: MutableArray<T> = MutableArray<T>(count, items[0]);
    let target: int = 0;
    for (let index: int = 0; index < items.length; index = index + 1) {
        if (included[index]) {
            result[target] = items[index];
            target = target + 1;
        }
    }
    return result.freeze();
}

export function Map<T, U>(items: T[], project: (item: T) => U): U[] {
    if (items.length == 0) {
        return [];
    }
    const first: U = project(items[0]);
    const result: MutableArray<U> = MutableArray<U>(items.length, first);
    for (let index: int = 1; index < items.length; index = index + 1) {
        result[index] = project(items[index]);
    }
    return result.freeze();
}

export function Reverse<T>(items: T[]): T[] {
    if (items.length == 0) {
        return [];
    }
    const result: MutableArray<T> = MutableArray<T>(items.length, items[0]);
    for (let index: int = 0; index < items.length; index = index + 1) {
        result[index] = items[items.length - index - 1];
    }
    return result.freeze();
}
