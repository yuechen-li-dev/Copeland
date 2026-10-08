const $schema: string = "copeland://tiny-farm/content/m21/scenes";

record table Scenes {
    id: string = ["dungeon-entrance"];
    label: string = ["Old Burrow"];
    width: number = [16];
    height: number = [12];
}

const $value = Scenes;
