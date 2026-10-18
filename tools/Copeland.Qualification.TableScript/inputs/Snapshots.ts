// Qualification-only field projections. The existing relation and summary
// implementations perform every calculation.
function scenarioPlanValues(plan: PurchasePlan): int[] {
    const products: PurchaseLine[] = plan.rows().select(purchaseLine);
    const values: MutableArray<int> = MutableArray<int>(products.length * 6);
    for (let index: int = 0; index < products.length; index = index + 1) {
        const row: PurchasePlan.Row = plan[index]!;
        values[index * 6] = row.productId;
        values[index * 6 + 1] = row.supplierId;
        values[index * 6 + 2] = row.leadDays;
        values[index * 6 + 3] = row.onHand;
        values[index * 6 + 4] = row.orderUnits;
        values[index * 6 + 5] = row.orderCents;
    }
    return values.freeze();
}

function scenarioPlanNames(plan: PurchasePlan): string[] {
    const products: PurchaseLine[] = plan.rows().select(purchaseLine);
    const names: MutableArray<string> = MutableArray<string>(products.length * 2, "");
    for (let index: int = 0; index < products.length; index = index + 1) {
        const row: PurchasePlan.Row = plan[index]!;
        names[index * 2] = row.productName;
        names[index * 2 + 1] = row.supplierName;
    }
    return names.freeze();
}
