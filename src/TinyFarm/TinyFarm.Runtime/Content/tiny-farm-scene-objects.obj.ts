const $schema: string = "copeland://tiny-farm/content/m7/objects";

enum OptionalText {
    None,
    Some(value: string),
}

// Rows are grouped by scene for readability. Scene ID plus object ID defines placement context.
record table SceneObjects {
    sceneId: string = [
        "overworld", "overworld", "overworld", "overworld",
        "farm", "farm", "farm", "farm", "farm",
        "town", "town", "town", "town",
        "general-store", "general-store", "general-store",
        "riverside", "riverside", "riverside"
    ];
    objectId: string = [
        "farm-entrance", "town-entrance", "riverside-entrance", "hill",
        "farm-exit", "farmhouse", "plot-1", "plot-2", "fence",
        "town-exit", "store-entrance", "well", "market-stall",
        "store-exit", "shop-counter", "shelves",
        "riverside-exit", "river", "reeds"
    ];
    kind: string = [
        "Portal", "Portal", "Portal", "Prop",
        "Portal", "Landmark", "Plot", "Plot", "Prop",
        "Portal", "Portal", "Landmark", "Prop",
        "Portal", "Shop", "Prop",
        "Portal", "Decoration", "Prop"
    ];
    label: string = [
        "Farm", "Town", "Riverside", "Hill",
        "Overworld", "Farmhouse", "Plot 1", "Plot 2", "Fence",
        "Overworld", "General Store", "Well", "Market Stall",
        "Town", "Seed Counter", "Shelves",
        "Overworld", "River", "Reeds"
    ];
    blocksMovement: boolean = [
        false, false, false, true,
        false, true, false, false, true,
        false, false, true, true,
        false, true, true,
        false, true, true
    ];
    semanticReference: OptionalText = [
        OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.Some("plot-1"), OptionalText.Some("plot-2"), OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.Some("general-store"), OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.None
    ];
}

const $value = SceneObjects;
