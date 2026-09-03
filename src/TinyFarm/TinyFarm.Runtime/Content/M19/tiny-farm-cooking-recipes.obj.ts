const $schema: string = "copeland://tiny-farm/content/m19/cooking-recipes";

record table CookingRecipes {
    recipeId: string = ["sauteed-hen-of-the-woods"];
    stationKind: string = ["Cooking"];
    outputProductId: string = ["sauteed-hen-of-the-woods"];
    outputCount: number = [1];
}

const $value = CookingRecipes;
