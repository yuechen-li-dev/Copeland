const $schema: string = "copeland://tiny-farm/content/m21/objects";

enum OptionalText {
    None,
    Some(value: string),
}

record table SceneObjects {
    sceneId: string = [
        "overworld",
        "dungeon-entrance", "dungeon-entrance", "dungeon-entrance",
        "dungeon-entrance", "dungeon-entrance"
    ];
    objectId: string = [
        "dungeon-entrance",
        "dungeon-exit", "dungeon-north-wall", "dungeon-south-wall",
        "dungeon-west-wall", "dungeon-east-wall"
    ];
    kind: string = [
        "Portal",
        "Portal", "Prop", "Prop", "Prop", "Prop"
    ];
    label: string = [
        "Old Burrow",
        "Overworld", "Cave Wall", "Cave Wall", "Cave Wall", "Cave Wall"
    ];
    blocksMovement: boolean = [
        false,
        false, true, true, true, true
    ];
    semanticReference: OptionalText = [
        OptionalText.None,
        OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None, OptionalText.None
    ];
}

const $value = SceneObjects;
