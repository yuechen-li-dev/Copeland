const $schema: string = "copeland://tiny-farm/tests/m7/valid/layout";
record table SceneLayout {
    sceneId: string = ["fixture"];
    objectId: string = ["marker"];
    x: number = [2];
    y: number = [2];
    width: number = [1];
    height: number = [1];
    layer: number = [0];
}
const $value = SceneLayout;
