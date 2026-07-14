// Authoring-profile trivia and ordinary integer spellings are accepted.
const $schema: string = "copeland://fixtures/primitives";
const $value = {
    truth: true,
    finite: -42,
    negativeZero: -0,
    nan: $number("7FF8000000000000"),
    positiveInfinity: $number("7FF0000000000000"),
    negativeInfinity: $number("FFF0000000000000"),
    text: "line\n😀",
};
