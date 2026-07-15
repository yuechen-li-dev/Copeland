interface Positioned {
    x: number;
}

record Point {
    x: number;
}

interface X {
    value: number;
}

interface Y {
    value: string;
}

function unknown<T extends Missing>(value: T): T {
    return value;
}

function wrongKind<T extends Point>(value: T): T {
    return value;
}

function repeated<T extends Positioned & Positioned>(value: T): number {
    return value.x;
}

function conflicting<T extends X & Y>(value: T): number {
    return value.value;
}
