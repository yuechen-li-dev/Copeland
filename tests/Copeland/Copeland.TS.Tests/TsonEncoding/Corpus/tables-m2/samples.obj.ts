// Authoring comments and formatting must not survive runtime encoding.
const $schema: string = "copeland://corpus/runtime-table-encoding";

record Point {
    name: string;
}

enum State {
    Off,
    Named(label: string),
}

record table Samples {
    active: boolean = [true, false, true, false, true];
    score: number = [
        $number("0000000000000000"),
        $number("8000000000000000"),
        $number("3FF8000000000000"),
        $number("7FF8000000000000"),
        $number("FFF0000000000000"),
    ];
    point: Point = [
        { name: "plain" },
        { name: "quote \" slash \\ newline\n" },
        { name: "雪" },
        { name: "😀" },
        { name: "𐐷" },
    ];
    state: State = [
        State.Off,
        State.Named("payload"),
        State.Named("雪"),
        State.Off,
        State.Named("array"),
    ];
    values: number[][] = [
        [],
        [[1, 2], []],
        [[], [0]],
        [[$number("7FF0000000000000")]],
        [[$number("7FF8000000000000")]],
    ];
}

const $value = Samples;
