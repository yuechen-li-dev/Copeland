// expected: COPE-TSON-TABLE-0004
const $schema: string = "copeland://fixtures/table/invalid";
record table Values { x: [1, "wrong"]; }
const $value = Values;
