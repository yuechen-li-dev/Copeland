export type CopelandProfile = "JavaScript";

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
