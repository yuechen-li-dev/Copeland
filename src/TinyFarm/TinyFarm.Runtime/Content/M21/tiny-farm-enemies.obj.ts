const $schema: string = "copeland://tiny-farm/content/m21/enemies";

record table Enemies {
    id: string = ["dungeon.slime-1"];
    kind: string = ["Slime"];
    sceneId: string = ["dungeon-entrance"];
    x: number = [8];
    y: number = [5];
    maxHealth: number = [1];
}

const $value = Enemies;
