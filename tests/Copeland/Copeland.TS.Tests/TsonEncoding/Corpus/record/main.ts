const $schema: string = "copeland://corpus/runtime-encoding";

record Detail {
    label: string;
}

enum Mode {
    Off,
    Named(detail: Detail),
}

record Settings {
    enabled: boolean;
    count: number;
    mode: Mode;
}

function encode(): string ! TsonEncodeError {
    const loaded: Settings = tsonAsset("./settings.obj.ts");
    return tsonEncode(loaded);
}
