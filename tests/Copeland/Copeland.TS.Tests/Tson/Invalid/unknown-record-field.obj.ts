// expected: COPE-TSON-0004
const $schema: string = "copeland://fixtures/invalid";
record User { name: string; }
const $value: User = { name: "Ada", extra: true };
