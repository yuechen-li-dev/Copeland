const $schema: string = "copeland://fixtures/table-assets";

record table Samples from tsonAsset("./samples.obj.ts") {
    active: boolean;
    score: number;
}

function first(): number {
    return Samples.score[0]!;
}
