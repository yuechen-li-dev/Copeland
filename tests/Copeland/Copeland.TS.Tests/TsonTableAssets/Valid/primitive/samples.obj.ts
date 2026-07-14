const $schema: string = "copeland://fixtures/table-assets";

record table Samples {
    active: boolean = [true];
    score: number = [42];
}

const $value = Samples;
