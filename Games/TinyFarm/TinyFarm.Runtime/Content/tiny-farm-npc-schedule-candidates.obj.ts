const $schema: string = "copeland://tiny-farm/content/m10/npc-schedule-candidates";

record table UtilityCandidates {
    windowId: string = ["mara.free-evening", "mara.free-evening"];
    anchorId: string = ["farm.home", "town.square"];
    considerationKind: string = ["current-location-stickiness", "current-location-stickiness"];
    baseScore: number = [60, 50];
    currentLocationBonus: number = [20, 40];
}

const $value = UtilityCandidates;
