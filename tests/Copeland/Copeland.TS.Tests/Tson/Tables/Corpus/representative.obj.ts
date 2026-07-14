const $schema: string = "copeland://corpus/table-m0b";

// The canonical projection removes comments and normalizes every number.
record Point {
    x: number;
    label: string;
}

enum State {
    Missing,
    Named(label: string),
}

record table Representative {
    enabled: [true, false];
    edge: number = [0, -0];
    text: string = ["quote: \"; snow: 雪; pair: 😀", "line\nnext"];
    point: Point = [{ x: $number("3FE0000000000000"), label: "a" }, { x: $number("7FF0000000000000"), label: "b" }];
    state: State = [State.Missing, State.Named("ready")];
    matrix: number[][] = [[[1], []], [[$number("FFF0000000000000")], [$number("7FF0000000000001")]]];
}

const $value = Representative;
