const $schema: string = "copeland://tiny-farm/content/m12/objects"; 

enum OptionalText {
    None,
    Some(value: string),
}

record table SceneObjects {
    sceneId: string = [
        "overworld", "overworld", "overworld", "overworld",
        "farm", "farm", "farm", "farm", "farm", "farm",
        "town", "town", "town", "town",
        "general-store", "general-store", "general-store",
        "riverside", "riverside", "riverside",
        "residence", "residence", "residence", "residence"
    ];
    objectId: string = [
        "farm-entrance", "town-entrance", "riverside-entrance", "hill",
        "farm-exit", "residence-entrance", "farmhouse", "plot-1", "plot-2", "fence",
        "town-exit", "store-entrance", "well", "market-stall",
        "store-exit", "shop-counter", "shelves",
        "riverside-exit", "river", "reeds",
        "residence-exit", "elias-bed", "mara-bed", "sela-bed"
    ];
    kind: string = [
        "Portal", "Portal", "Portal", "Prop",
        "Portal", "Portal", "Landmark", "Plot", "Plot", "Prop",
        "Portal", "Portal", "Landmark", "Prop",
        "Portal", "Shop", "Prop",
        "Portal", "Decoration", "Prop",
        "Portal", "Bed", "Bed", "Bed"
    ];
    label: string = [
        "Farm", "Town", "Riverside", "Hill",
        "Overworld", "Enter Hearth House", "Farmhouse", "Plot 1", "Plot 2", "Fence",
        "Overworld", "General Store", "Well", "Market Stall",
        "Town", "Seed Counter", "Shelves",
        "Overworld", "River", "Reeds",
        "Leave Hearth House", "Elias's Bed", "Mara's Bed", "Sela's Bed"
    ];
    blocksMovement: boolean = [
        false, false, false, true,
        false, false, true, false, false, true,
        false, false, true, true,
        false, true, true,
        false, true, true,
        false, true, true, true
    ];
    semanticReference: OptionalText = [
        OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.Some("plot-1"), OptionalText.Some("plot-2"), OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.Some("general-store"), OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.None,
        OptionalText.None, OptionalText.Some("elias"), OptionalText.Some("mara"), OptionalText.Some("sela")
    ];
}

const $value = SceneObjects;
