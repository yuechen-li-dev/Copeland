const $schema: string = "copeland://tiny-farm/content/m21/routes";

record table SceneRoutes {
    routeId: string = ["overworld-dungeon", "dungeon-overworld"];
    sourceScene: string = ["overworld", "dungeon-entrance"];
    triggerObject: string = ["dungeon-entrance", "dungeon-exit"];
    targetScene: string = ["dungeon-entrance", "overworld"];
    targetAnchor: string = ["dungeon-entrance.entry", "overworld.from-dungeon"];
    interactionLabel: string = ["ENTER OLD BURROW", "LEAVE OLD BURROW"];
}

const $value = SceneRoutes;
