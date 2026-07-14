const $schema: string = "copeland://corpus/runtime-array-encoding";

record Detail {
    label: string;
}

enum Signal {
    Idle,
    Text(value: string),
    DetailValue(detail: Detail),
}

record Packet {
    emptyNumbers: number[];
    booleans: boolean[];
    numbers: number[];
    texts: string[];
    nested: number[][];
    details: Detail[];
    signals: Signal[];
    emptyDetails: Detail[];
}

function encode(): string ! TsonEncodeError {
    const loaded: Packet = tsonAsset("./packet.obj.ts");
    return tsonEncode(loaded);
}
