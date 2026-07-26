export type Portions = number;

export interface HasPortions {
    portions: number;
}

export record PortionPlan {
    portions: Portions;
    label: string;
}

export function PortionCount<T extends HasPortions>(plan: T): Portions {
    return plan.portions;
}

export function PlannedPortions(portions: Portions): Portions {
    const plan: PortionPlan = {
        portions,
        label: "weekday",
    };
    return PortionCount(plan);
}

export function ExplicitPlannedPortions(portions: Portions): Portions {
    const plan: PortionPlan = {
        portions,
        label: "weekend",
    };
    return PortionCount<PortionPlan>(plan);
}

export function DoublePortions(portions: number[]): number[] {
    return batch portions as portion {
        return portion * 2;
    };
}

export function* CookingSlots(count: number): Iterable<number> {
    let slot: number = 0;

    while (slot < count) {
        yield slot;
        slot = slot + 1;
    }
}

export function SumCookingSlots(count: number): number {
    let total: number = 0;

    for (const slot of CookingSlots(count)) {
        total = total + slot;
    }

    return total;
}
