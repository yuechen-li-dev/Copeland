// Qualification-only projections of typed records across the C# host boundary.
// These functions copy fields; they do not recalculate totals or groups.
function typedTotals(): int[] {
    return projectTotals(purchaseTotals());
}

function projectTotals(total: PurchaseTotals): int[] {
    return [total.totalCents, total.units, total.lines];
}

function typedSupplierValues(): int[] {
    return projectSupplierValues(supplierSummaries());
}

function projectSupplierValues(suppliers: SupplierSummary[]): int[] {
    const values: MutableArray<int> = MutableArray<int>(suppliers.length * 4);
    for (let index: int = 0; index < suppliers.length; index = index + 1) {
        const supplier: SupplierSummary = suppliers[index];
        values[index * 4] = supplier.supplierId;
        values[index * 4 + 1] = supplier.totalCents;
        values[index * 4 + 2] = supplier.units;
        values[index * 4 + 3] = supplier.lines;
    }
    return values.freeze();
}

function typedSupplierNames(): string[] {
    return projectSupplierNames(supplierSummaries());
}

function projectSupplierNames(suppliers: SupplierSummary[]): string[] {
    const names: MutableArray<string> = MutableArray<string>(suppliers.length, "");
    for (let index: int = 0; index < suppliers.length; index = index + 1) {
        names[index] = suppliers[index].supplierName;
    }
    return names.freeze();
}
