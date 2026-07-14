// expected: COPE-TSON-TABLE-0001
const $schema: string = "copeland://fixtures/table/invalid";
record table Values { x: [1]; }
function run(): number { return 1; }
const $value = Values;
