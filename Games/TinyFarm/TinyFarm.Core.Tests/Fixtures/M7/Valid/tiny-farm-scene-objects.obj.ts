const $schema: string = "copeland://tiny-farm/tests/m7/valid/objects";
enum OptionalText { None, Some(value: string), }
record table SceneObjects {
    sceneId: string = ["fixture"];
    objectId: string = ["marker"];
    kind: string = ["Prop"];
    label: string = ["Marker"];
    blocksMovement: boolean = [false];
    semanticReference: OptionalText = [OptionalText.None];
}
const $value = SceneObjects;
