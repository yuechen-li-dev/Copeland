const $schema: string = "copeland://tiny-farm/content/m14/anchors";

enum OptionalText {
    None,
    Some(value: string),
}

// Positions are authored in tiles and materialized at tile centers in ScenePosition units.
record table SceneAnchors {
    anchorId: string = [
        "overworld.from-farm", "overworld.from-town", "overworld.from-riverside",
        "farm.from-overworld", "farm.start", "farm.home", "farm.work-area", "farm.wander-a", "farm.wander-b",
        "town.south-gate", "town.from-store", "town.square",
        "general-store.door", "general-store.counter",
        "riverside.from-overworld", "riverside.meeting-point",
        "residence.from-farm", "elias.home-bed", "mara.home-bed", "sela.home-bed", "riverside.elias-bench"
    ];
    sceneId: string = [
        "overworld", "overworld", "overworld",
        "farm", "farm", "farm", "farm", "farm", "farm",
        "town", "town", "town",
        "general-store", "general-store",
        "riverside", "riverside",
        "residence", "residence", "residence", "residence", "riverside"
    ];
    x: number = [3, 10, 18, 16, 6, 4, 6, 5, 10, 10, 16, 12, 5, 5, 2, 5, 9, 2, 6, 10, 3
    ];
    y: number = [7, 5, 9, 6, 5, 7, 5, 8, 9, 12, 4, 7, 6, 3, 5, 5, 7, 2, 2, 2, 8
    ];
    kind: string = [
        "Spawn", "Spawn", "Spawn", "Spawn", "Spawn", "Home", "Work", "Wander", "Wander",
        "Spawn", "Spawn", "Social", "Spawn", "ShopCounter", "Spawn", "Social",
        "Spawn", "Rest", "Rest", "Rest", "Social"
    ];
    semanticLocation: OptionalText = [
        OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.Some("farmhouse"), OptionalText.Some("farmhouse"), OptionalText.Some("farmhouse"), OptionalText.Some("farmhouse"),
        OptionalText.None, OptionalText.None, OptionalText.Some("town-square"),
        OptionalText.None, OptionalText.Some("general-store"),
        OptionalText.None, OptionalText.Some("riverside"),
        OptionalText.Some("farmhouse"), OptionalText.Some("farmhouse"), OptionalText.Some("farmhouse"), OptionalText.Some("farmhouse"), OptionalText.Some("riverside")
    ];
    semanticObject: OptionalText = [
        OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.Some("shop-counter"),
        OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.Some("elias-bed"), OptionalText.Some("mara-bed"), OptionalText.Some("sela-bed"), OptionalText.None
    ];
    facing: OptionalText = [
        OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.Some("Up"), OptionalText.Some("Up"), OptionalText.Some("Up"), OptionalText.Some("Right")
    ];
    arrivalRadiusUnits: number = [128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 768, 768, 768, 128
    ];
}

const $value = SceneAnchors;
