interface Positioned {
    x: number;
}

enum Choice {
    Value,
}

record table Samples {
    x: [1];
}

function use<T extends Positioned>(value: T): number {
    return value.x;
}

const a: number = use<number>(1);
const b: number = use<number[]>([1]);
const c: number = use<number ! string>(ok(1));
const d: number = use<Choice>(Choice.Value);
const e: number = use<Samples>(Samples);
const f: number = use<column number>(Samples.x);
