const $schema: string = "copeland://fixtures/table-assets";

record table Missing from tsonAsset("./missing.tson") {
    value: number;
}
