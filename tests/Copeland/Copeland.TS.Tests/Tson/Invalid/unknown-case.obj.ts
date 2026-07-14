// expected: COPE-TSON-0004
const $schema: string = "copeland://fixtures/invalid";
enum State { Ready, }
const $value = State.Missing;
