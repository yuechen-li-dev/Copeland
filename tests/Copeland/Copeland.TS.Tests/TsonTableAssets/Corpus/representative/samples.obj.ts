const $schema: string = "copeland://corpus/tson-table-m1";

// Authored comments affect dependency evidence but not the projected table.
record Point {
    x: number;
    label: string;
}

enum State {
    Missing,
    Named(label: string),
}

record table Samples {
    active: boolean = [true, false];
    score: number = [$number("8000000000000000"), $number("7FF8000000000001")];
    label: string = ["quote \" slash \\ newline\n", "雪 😀"];
    point: Point = [{ x: 1, label: "first" }, { x: 2, label: "second" }];
    state: State = [State.Missing, State.Named("ready")];
    values: number[][] = [[[], [1, 2]], [[3], []]];
}

const $value = Samples;
