const $schema: string = "copeland://tiny-farm/content/m14/npc-schedule-candidates";

record table UtilityCandidates {
    windowId: string = [
        "mara.free-evening", "mara.free-evening",
        "elias.morning-work", "elias.morning-work", "elias.morning-work",
        "elias.free-evening", "elias.free-evening",
        "sela.free-evening", "sela.free-evening"
    ];
    anchorId: string = [
        "mara.home-bed", "town.square",
        "elias.home-bed", "farm.wander-a", "farm.wander-b",
        "elias.home-bed", "farm.work-area",
        "sela.home-bed", "general-store.counter"
    ];
    considerationKind: string = [
        "energy-rest", "current-location-stickiness",
        "energy-rest", "local-wander", "local-wander",
        "energy-rest", "current-location-stickiness",
        "energy-rest", "current-location-stickiness"
    ];
    baseScore: number = [10, 50, 10, 50, 50, 10, 50, 10, 50];
    currentLocationBonus: number = [0, 20, 0, 10, 10, 0, 20, 0, 20];
}

const $value = UtilityCandidates;
