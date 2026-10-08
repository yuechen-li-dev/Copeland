const $schema: string = "copeland://tiny-farm/content/m7/routes";

// Routes bind semantic IDs only. Target coordinates come exclusively from target anchors.
record table SceneRoutes {
    routeId: string = [
        "overworld-farm", "overworld-town", "overworld-riverside",
        "farm-overworld", "town-overworld", "town-store", "store-town", "riverside-overworld"
    ];
    sourceScene: string = [
        "overworld", "overworld", "overworld", "farm", "town", "town", "general-store", "riverside"
    ];
    triggerObject: string = [
        "farm-entrance", "town-entrance", "riverside-entrance", "farm-exit",
        "town-exit", "store-entrance", "store-exit", "riverside-exit"
    ];
    targetScene: string = [
        "farm", "town", "riverside", "overworld", "overworld", "general-store", "town", "overworld"
    ];
    targetAnchor: string = [
        "farm.from-overworld", "town.south-gate", "riverside.from-overworld", "overworld.from-farm",
        "overworld.from-town", "general-store.door", "town.from-store", "overworld.from-riverside"
    ];
    interactionLabel: string = [
        "ENTER FARM", "ENTER TOWN", "ENTER RIVERSIDE", "RETURN TO OVERWORLD",
        "RETURN TO OVERWORLD", "ENTER STORE", "LEAVE STORE", "RETURN TO OVERWORLD"
    ];
}

const $value = SceneRoutes;
