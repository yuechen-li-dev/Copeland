const $schema: string = "copeland://tiny-farm/content/m9/npc-schedules";

// Half-open intervals answer where each NPC belongs at a minute of a seven-day week.
record table NpcSchedules {
    actorId: string = [
        "mara", "mara", "mara", "mara", "mara",
        "elias", "elias", "elias",
        "sela", "sela", "sela"
    ];
    day: string = [
        "Every", "Every", "Every", "Day6", "Day7",
        "Every", "Every", "Every",
        "Every", "Every", "Every"
    ];
    startMinute: number = [0, 720, 1020, 540, 600, 0, 720, 1080, 0, 480, 1080];
    endMinuteExclusive: number = [720, 1020, 1440, 1020, 1020, 720, 1080, 1440, 480, 1080, 1440];
    anchorId: string = [
        "town.square", "riverside.meeting-point", "farm.home", "general-store.counter", "riverside.meeting-point",
        "farm.work-area", "riverside.meeting-point", "farm.work-area",
        "farm.home", "general-store.counter", "farm.home"
    ];
    priority: number = [0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0];
    reason: string = [
        "daily-morning-town", "daily-afternoon-riverside", "daily-evening-home", "day-6-store", "day-7-riverside",
        "daily-morning-work", "daily-afternoon-riverside", "daily-evening-work",
        "daily-morning-home", "daily-store", "daily-evening-home"
    ];
}

const $value = NpcSchedules;
