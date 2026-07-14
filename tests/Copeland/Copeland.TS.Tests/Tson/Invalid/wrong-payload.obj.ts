// expected: COPE-TSON-0004
const $schema: string = "copeland://fixtures/invalid";
enum State { Named(name: string), }
const $value = State.Named(1);
