const $schema: string = "copeland://tiny-farm/content/m20";

record table Products {
    id: string = ["hen-of-the-woods", "sauteed-hen-of-the-woods", "turnip", "turnip-seed", "wood"];
    name: string = ["Hen-of-the-Woods", "Sautéed Hen-of-the-Woods", "turnip", "turnip seed", "Wood"];
    buyPrice: number = [0, 0, 0, 2, 0];
    sellPrice: number = [3, 6, 5, 0, 2];
    cropId: string = ["", "", "turnip", "", ""];
    seedItemId: string = ["", "", "turnip-seed", "", ""];
    harvestItemId: string = ["", "", "turnip", "", ""];
    growthDays: number = [0, 0, 3, 0, 0];
    waterRequirement: number = [0, 0, 1, 0, 0];
    yieldCount: number = [0, 0, 2, 0, 0];
}

const $value = Products;
