const $schema: string = "copeland://fixtures/table/binary64-unicode";
record table Edges {
    value: number = [$number("0000000000000000"), $number("8000000000000000"), $number("3FD0000000000000"), $number("7FF0000000000001"), $number("7FF0000000000000"), $number("FFF0000000000000")];
    text: string = ["zero", "minus zero", "quarter", "NaN", "雪", "pair 😀 and quote \""];
}
const $value = Edges;
