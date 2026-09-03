const $schema: string = "copeland://tiny-farm/content/m20/trees";

record table Trees {
    id: string = ["farm-tree"];
    sceneId: string = ["farm"];
    x: number = [11];
    y: number = [5];
    yieldProductId: string = ["wood"];
    yieldCount: number = [1];
}

const $value = Trees;
