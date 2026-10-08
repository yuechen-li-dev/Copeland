const $schema: string = "copeland://tiny-farm/content/m7/anchors";

enum OptionalText {
    None,
    Some(value: string),
}

// Positions are authored in tiles and materialized at tile centers in ScenePosition units.
record table SceneAnchors {
    anchorId: string = [
        "overworld.from-farm", "overworld.from-town", "overworld.from-riverside",
        "farm.from-overworld", "farm.start", "farm.home", "farm.work-area",
        "town.south-gate", "town.from-store", "town.square",
        "general-store.door", "general-store.counter",
        "riverside.from-overworld", "riverside.meeting-point"
    ];
    sceneId: string = [
        "overworld", "overworld", "overworld",
        "farm", "farm", "farm", "farm",
        "town", "town", "town",
        "general-store", "general-store",
        "riverside", "riverside"
    ];
    x: number = [3, 10, 18, 16, 6, 4, 6, 10, 16, 12, 5, 5, 2, 5];
    y: number = [7, 5, 9, 6, 5, 7, 5, 12, 4, 7, 6, 3, 5, 5];
    kind: string = [
        "Spawn", "Spawn", "Spawn", "Spawn", "Spawn", "Home", "Work",
        "Spawn", "Spawn", "Social", "Spawn", "ShopCounter", "Spawn", "Social"
    ];
    semanticLocation: OptionalText = [
        OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.Some("farmhouse"), OptionalText.Some("farmhouse"),
        OptionalText.None, OptionalText.None, OptionalText.Some("town-square"),
        OptionalText.None, OptionalText.Some("general-store"),
        OptionalText.None, OptionalText.Some("riverside")
    ];
    semanticObject: OptionalText = [
        OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.Some("shop-counter"),
        OptionalText.None, OptionalText.None
    ];
    facing: OptionalText = [
        OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.None
    ];
    arrivalRadiusUnits: number = [128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128];
}

const $value = SceneAnchors;
