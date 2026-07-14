const $schema: string = "copeland://corpus/runtime-table-encoding";

record Point {
    name: string;
}

enum State {
    Off,
    Named(label: string),
}

record table Empty from tsonAsset("./empty.obj.ts") {
    active: boolean;
    note: string;
}

record table Samples from tsonAsset("./samples.obj.ts") {
    active: boolean;
    score: number;
    point: Point;
    state: State;
    values: number[][];
}

function encode(): string ! TsonEncodeError {
    return tsonEncode(Samples);
}

function encodeEmpty(): string ! TsonEncodeError {
    return tsonEncode(Empty);
}
