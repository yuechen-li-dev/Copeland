const $schema: string = "copeland://tiny-farm/content/m21/anchors";

enum OptionalText {
    None,
    Some(value: string),
}

record table SceneAnchors {
    anchorId: string = [
        "overworld.from-dungeon", "dungeon-entrance.entry", "dungeon-entrance.slime-approach"
    ];
    sceneId: string = ["overworld", "dungeon-entrance", "dungeon-entrance"];
    x: number = [18, 2, 7];
    y: number = [2, 6, 5];
    kind: string = ["Spawn", "Spawn", "Encounter"];
    semanticLocation: OptionalText = [OptionalText.None, OptionalText.None, OptionalText.None];
    semanticObject: OptionalText = [OptionalText.None, OptionalText.None, OptionalText.None];
    facing: OptionalText = [
        OptionalText.Some("Left"), OptionalText.Some("Right"), OptionalText.Some("Right")
    ];
    arrivalRadiusUnits: number = [128, 128, 128];
}

const $value = SceneAnchors;
