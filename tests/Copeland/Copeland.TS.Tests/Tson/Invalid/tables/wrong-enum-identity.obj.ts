// expected: COPE-TSON-TABLE-0004
const $schema: string = "copeland://fixtures/table/invalid";
enum First { A, }
enum Second { A, }
record table Values { item: First = [Second.A]; }
const $value = Values;
