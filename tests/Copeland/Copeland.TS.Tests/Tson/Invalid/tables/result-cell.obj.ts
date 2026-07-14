// expected: COPE-TSON-TABLE-0002
const $schema: string = "copeland://fixtures/table/invalid";
record table Values { x: number ! string = []; }
const $value = Values;
