// Broader TSON cells bind, but the current C# query carrier cannot map arrays.
const $schema: string = "copeland://tests/tablescript-friction/array-query-boundary";
enum Flag { On, Off, }
record table Flags {
    value: Flag[] = [[Flag.On], [Flag.Off]];
}
const $value = Flags;
