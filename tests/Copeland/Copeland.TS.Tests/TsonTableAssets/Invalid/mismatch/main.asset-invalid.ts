const $schema: string = "copeland://fixtures/table-assets";

record table Expected from tsonAsset("./actual.obj.ts") {
    value: number;
}
