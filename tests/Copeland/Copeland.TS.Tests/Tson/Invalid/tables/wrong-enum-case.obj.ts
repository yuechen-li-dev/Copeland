// expected: COPE-TSON-TABLE-0004
const $schema: string = "copeland://fixtures/table/invalid";
enum State { Ready, }
record table Values { item: State = [State.Unknown]; }
const $value = Values;
