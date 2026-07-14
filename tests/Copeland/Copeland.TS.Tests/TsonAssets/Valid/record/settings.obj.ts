const $schema: string = "copeland://fixtures/assets-record";

// Authoring fixture intentionally uses compact, noncanonical layout.
record Settings { title: string; enabled: boolean; }
const $value: Settings = { enabled: true, title: "fixture" };
