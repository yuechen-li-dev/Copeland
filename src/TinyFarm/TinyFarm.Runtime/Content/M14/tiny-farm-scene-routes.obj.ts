const $schema: string = "copeland://tiny-farm/content/m14/routes";

// Routes bind semantic IDs only. Target coordinates come exclusively from target anchors.
record table SceneRoutes {
    routeId: string = [
        "overworld-farm", "overworld-town", "overworld-riverside",
        "farm-overworld", "farm-residence", "town-overworld", "town-store", "store-town", "riverside-overworld", "residence-farm"
    ];
    sourceScene: string = [
        "overworld", "overworld", "overworld", "farm", "farm", "town", "town", "general-store", "riverside", "residence"
    ];
    triggerObject: string = [
        "farm-entrance", "town-entrance", "riverside-entrance", "farm-exit", "residence-entrance",
        "town-exit", "store-entrance", "store-exit", "riverside-exit", "residence-exit"
    ];
    targetScene: string = [
        "farm", "town", "riverside", "overworld", "residence", "overworld", "general-store", "town", "overworld", "farm"
    ];
    targetAnchor: string = [
        "farm.from-overworld", "town.south-gate", "riverside.from-overworld", "overworld.from-farm",
        "residence.from-farm", "overworld.from-town", "general-store.door", "town.from-store", "overworld.from-riverside", "farm.start"
    ];
    interactionLabel: string = [
        "ENTER FARM", "ENTER TOWN", "ENTER RIVERSIDE", "RETURN TO OVERWORLD",
        "ENTER HEARTH HOUSE", "RETURN TO OVERWORLD", "ENTER STORE", "LEAVE STORE", "RETURN TO OVERWORLD", "LEAVE HEARTH HOUSE"
    ];
}

const $value = SceneRoutes;
