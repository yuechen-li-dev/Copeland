const $schema: string = "copeland://tiny-farm/content/m19/cooking-stations";

record table CookingStations {
    id: string = ["hearth-house-kitchen"];
    sceneId: string = ["residence"];
    x: number = [6];
    y: number = [4];
    label: string = ["Kitchen Stove"];
}

const $value = CookingStations;
