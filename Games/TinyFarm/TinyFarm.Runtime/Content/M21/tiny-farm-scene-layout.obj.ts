const $schema: string = "copeland://tiny-farm/content/m21/layout";

record table SceneLayout {
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
    x: number = [19, 1, 0, 0, 0, 15];
    y: number = [2, 5, 0, 11, 0, 0];
    width: number = [1, 1, 16, 16, 1, 1];
    height: number = [1, 2, 1, 1, 12, 12];
    layer: number = [0, 0, 0, 0, 0, 0];
}

const $value = SceneLayout;
