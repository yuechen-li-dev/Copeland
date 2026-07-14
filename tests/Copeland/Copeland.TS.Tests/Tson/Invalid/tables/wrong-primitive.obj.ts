// expected: COPE-TSON-TABLE-0004
const $schema: string = "copeland://fixtures/table/invalid";
record table Values { x: number = ["wrong"]; }
const $value = Values;
