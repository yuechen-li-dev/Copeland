import { BuildDailySummary } from "./RecipeBook";
import { DoublePortions, ExplicitPlannedPortions, PlannedPortions, SumCookingSlots } from "./Planning";

export function Run(name: string, portions: int, calories: float): string {
    return BuildDailySummary(name, portions, calories);
}

export function PlanPortions(portions: number): number {
    return PlannedPortions(portions) + ExplicitPlannedPortions(portions + 1);
}

export function DoubledPortions(portions: number[]): number[] {
    return DoublePortions(portions);
}

export function CookingSlotTotal(count: number): number {
    return SumCookingSlots(count);
}
