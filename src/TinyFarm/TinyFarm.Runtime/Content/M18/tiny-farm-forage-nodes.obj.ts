const $schema: string = "copeland://tiny-farm/content/m18/forage-nodes";

record table ForageNodes {
    id: string = ["riverside-hen-of-the-woods"];
    sceneId: string = ["riverside"];
    x: number = [6];
    y: number = [6];
    productId: string = ["hen-of-the-woods"];
    yieldCount: number = [1];
}

const $value = ForageNodes;
