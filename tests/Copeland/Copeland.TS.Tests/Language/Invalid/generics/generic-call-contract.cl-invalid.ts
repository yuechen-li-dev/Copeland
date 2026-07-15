interface Positioned {
    x: number;
}

function identity<T>(value: T): T {
    return value;
}

function use<T extends Positioned>(value: T): number {
    return value.x;
}

const inferred: number = identity(1);
const wrongCount: number = identity<number, string>(1);
const nongeneric: number = use<number>(1);
const explicitInterface: number = identity<Positioned>(1);
