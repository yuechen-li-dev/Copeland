const $schema: string = "copeland://fixtures/type-alias/v1";

type SettingsAlias = Settings;

record Settings {
    enabled: boolean;
}

function encode(settings: SettingsAlias): string ! TsonEncodeError {
    return tsonEncode(settings);
}
