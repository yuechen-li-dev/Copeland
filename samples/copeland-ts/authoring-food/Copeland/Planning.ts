type Portions = number;

interface HasPortions {
    portions: number;
}

record PortionPlan {
    portions: Portions;
    label: string;
}

function PortionCount<T extends HasPortions>(plan: T): Portions {
    return plan.portions;
}

function PlannedPortions(portions: Portions): Portions {
    const plan: PortionPlan = {
        portions,
        label: "weekday",
    };
    return PortionCount(plan);
}

function ExplicitPlannedPortions(portions: Portions): Portions {
    const plan: PortionPlan = {
        portions,
        label: "weekend",
    };
    return PortionCount<PortionPlan>(plan);
}

function DoublePortions(portions: number[]): number[] {
    return batch portions as portion {
        return portion * 2;
    };
}

function* CookingSlots(count: number): Iterable<number> {
    let slot: number = 0;

    while (slot < count) {
        yield slot;
        slot = slot + 1;
    }
}

function SumCookingSlots(count: number): number {
    let total: number = 0;

    for (const slot of CookingSlots(count)) {
        total = total + slot;
    }

    return total;
}
