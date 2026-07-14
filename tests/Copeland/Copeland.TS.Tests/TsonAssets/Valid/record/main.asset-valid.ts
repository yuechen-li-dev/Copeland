const $schema: string = "copeland://fixtures/assets-record";

record Settings {
    title: string;
    enabled: boolean;
}

function load(): Settings {
    const settings: Settings = tsonAsset("./settings.obj.ts");
    return settings;
}
