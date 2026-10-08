const $schema: string = "copeland://tiny-farm/content/m19/cooking-recipe-inputs";

record table CookingRecipeInputs {
    recipeId: string = ["sauteed-hen-of-the-woods"];
    productId: string = ["hen-of-the-woods"];
    count: number = [1];
}

const $value = CookingRecipeInputs;
