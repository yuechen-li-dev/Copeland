const $schema: string = "copeland://tiny-farm/content/m12/npc-schedule-candidates";

record table UtilityCandidates {
    windowId: string = [
        "mara.free-evening", "mara.free-evening",
        "elias.free-evening", "elias.free-evening",
        "sela.free-evening", "sela.free-evening"
    ];
    anchorId: string = [
        "mara.home-bed", "town.square",
        "elias.home-bed", "farm.work-area",
        "sela.home-bed", "general-store.counter"
    ];
    considerationKind: string = [
        "energy-rest", "current-location-stickiness",
        "energy-rest", "current-location-stickiness",
        "energy-rest", "current-location-stickiness"
    ];
    baseScore: number = [10, 50, 10, 50, 10, 50];
    currentLocationBonus: number = [0, 20, 0, 20, 0, 20];
}

const $value = UtilityCandidates;
