const $schema: string = "copeland://tiny-farm/tests/m7/valid/anchors";
enum OptionalText { None, Some(value: string), }
record table SceneAnchors {
    anchorId: string = ["fixture.spawn"];
    sceneId: string = ["fixture"];
    x: number = [1];
    y: number = [1];
    kind: string = ["Spawn"];
    semanticLocation: OptionalText = [OptionalText.None];
    semanticObject: OptionalText = [OptionalText.None];
    facing: OptionalText = [OptionalText.None];
    arrivalRadiusUnits: number = [128];
}
const $value = SceneAnchors;
