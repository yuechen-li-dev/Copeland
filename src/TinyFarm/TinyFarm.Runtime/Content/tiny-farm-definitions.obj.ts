const $schema: string = "copeland://tiny-farm/content/m2";

record table Products {
    id: string = ["turnip", "turnip-seed"];
    name: string = ["turnip", "turnip seed"];
    buyPrice: number = [0, 2];
    sellPrice: number = [5, 0];
    cropId: string = ["turnip", ""];
    seedItemId: string = ["turnip-seed", ""];
    harvestItemId: string = ["turnip", ""];
    growthDays: number = [3, 0];
    waterRequirement: number = [1, 0];
    yieldCount: number = [2, 0];
}

const $value = Products;
