const $schema: string = "copeland://fixtures/assets-missing";

record Settings {
    value: number;
}

function load(): Settings {
    const settings: Settings = tsonAsset("./missing.tson");
    return settings;
}
