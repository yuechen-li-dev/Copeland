const $schema: string = "copeland://tiny-farm/content/m10/npc-schedules";

enum ScheduleDay { Every, Day(value: number), }
enum ScheduleRegime { Required, Open, }

// Half-open intervals answer where each NPC belongs at a minute of a seven-day week.
record table NpcSchedules {
    windowId: string = [
        "mara.morning-town", "mara.afternoon-riverside", "mara.free-evening", "mara.required-home", "mara.day6-store", "mara.day7-riverside",
        "elias.morning-work", "elias.afternoon-riverside", "elias.evening-work",
        "sela.morning-home", "sela.store", "sela.evening-home"
    ];
    actorId: string = [
        "mara", "mara", "mara", "mara", "mara", "mara",
        "elias", "elias", "elias",
        "sela", "sela", "sela"
    ];
    day: ScheduleDay = [
        ScheduleDay.Every, ScheduleDay.Every, ScheduleDay.Every, ScheduleDay.Every, ScheduleDay.Day(6), ScheduleDay.Day(7),
        ScheduleDay.Every, ScheduleDay.Every, ScheduleDay.Every,
        ScheduleDay.Every, ScheduleDay.Every, ScheduleDay.Every
    ];
    startMinute: number = [0, 720, 1020, 1320, 540, 600, 0, 720, 1080, 0, 480, 1080];
    endMinuteExclusive: number = [720, 1020, 1320, 1440, 1020, 1020, 720, 1080, 1440, 480, 1080, 1440];
    regime: ScheduleRegime = [
        ScheduleRegime.Required, ScheduleRegime.Required, ScheduleRegime.Open, ScheduleRegime.Required, ScheduleRegime.Required, ScheduleRegime.Required,
        ScheduleRegime.Required, ScheduleRegime.Required, ScheduleRegime.Required,
        ScheduleRegime.Required, ScheduleRegime.Required, ScheduleRegime.Required
    ];
    requiredAnchorId: string = [
        "town.square", "riverside.meeting-point", "", "farm.home", "general-store.counter", "riverside.meeting-point",
        "farm.work-area", "riverside.meeting-point", "farm.work-area",
        "farm.home", "general-store.counter", "farm.home"
    ];
    priority: number = [0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0];
    reason: string = [
        "daily-morning-town", "daily-afternoon-riverside", "daily-evening-home", "daily-evening-home", "day-6-store", "day-7-riverside",
        "daily-morning-work", "daily-afternoon-riverside", "daily-evening-work",
        "daily-morning-home", "daily-store", "daily-evening-home"
    ];
}

const $value = NpcSchedules;
