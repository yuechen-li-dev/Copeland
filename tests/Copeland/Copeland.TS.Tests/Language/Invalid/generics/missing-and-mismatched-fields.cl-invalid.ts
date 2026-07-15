interface Positioned {
    x: number;
    y: number;
}

record MissingY {
    x: number;
}

record WrongY {
    x: number;
    y: string;
}

function use<T extends Positioned>(value: T): number {
    return value.x + value.y;
}

const a: number = use<MissingY>({ x: 1 });
const b: number = use<WrongY>({ x: 1, y: "bad" });
