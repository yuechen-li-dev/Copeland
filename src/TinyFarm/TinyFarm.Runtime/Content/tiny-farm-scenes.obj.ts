const $schema: string = "copeland://tiny-farm/content/m7/scenes";

// Declaration order is for authoring readability only. Stable scene IDs define identity.
record table Scenes {
    id: string = ["overworld", "farm", "town", "general-store", "riverside"];
    label: string = ["Overworld", "Farm", "Town", "General Store", "Riverside"];
    width: number = [22, 18, 20, 10, 16];
    height: number = [14, 12, 14, 8, 10];
}

const $value = Scenes;
