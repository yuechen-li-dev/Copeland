// expected: COPE-TSON-TABLE-0004
const $schema: string = "copeland://fixtures/table/invalid";
enum State { Named(label: string), }
record table Values { item: State = [State.Named()]; }
const $value = Values;
