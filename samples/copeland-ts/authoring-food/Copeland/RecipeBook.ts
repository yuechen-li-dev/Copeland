using Copeland.Authoring.Food;

export record Recipe {
    name: string;
    portions: int;
    calories: float;
}

export enum PrepDecision {
    Cook(recipe: Recipe),
    Skip(reason: string),
}

export function BuildDailySummary(name: string, portions: int, calories: float): string {
    const recipe: Recipe = {
        name: KitchenText.Normalize(name),
        portions,
        calories,
    };
    const decision: PrepDecision = PrepDecision.Cook(recipe);

    return match decision {
        Cook(selected) => BuildCookSummary(selected),
        Skip(reason) => "Skipped: " + reason,
    };
}

function BuildCookSummary(recipe: Recipe): string {
    const highlighted: string = Highlight(recipe.name);
    return `${highlighted} serves ${recipe.portions} for ${recipe.calories} calories`;
}

function Highlight(value: string): string {
    csharp {
        return KitchenText.Emphasize(value);
    }
}
