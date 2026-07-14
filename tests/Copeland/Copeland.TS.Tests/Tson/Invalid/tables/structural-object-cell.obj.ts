// expected: COPE-TSON-TABLE-0004
const $schema: string = "copeland://fixtures/table/invalid";
record table Values { x: number = [{ "x": 1 }]; }
const $value = Values;
