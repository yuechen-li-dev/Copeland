// Synthetic café inputs. Cents and Units are transparent aliases, not branded types.
type Cents = int;
type Units = int;

export enum Purchasing {
    Active,
    Paused,
}

export record table Suppliers {
    key id: int;
    name: string;
    leadDays: int;
} = [
    { id: 10, name: "North Roastery", leadDays: 3 },
    { id: 20, name: "Leaf House", leadDays: 5 },
    { id: 30, name: "Pantry Co", leadDays: 2 },
];

export record table Products {
    key id: int = [100, 101, 102, 103, 104, 105];
    reference supplierId: int -> Suppliers.id = [10, 10, 20, 30, 30, 10];
    name: string = ["Espresso beans", "Filter beans", "Earl Grey", "Oat milk", "Cocoa", "Decaf beans"];
    costCents: Cents = [925, 750, 310, 250, 480, 1100];
    reorderPoint: Units = [8 ... 2, 6, 12, 8 ... 2];
    targetStock: Units = [24, 24, 18, 36, 20, 24];
    purchasing: Purchasing = [Purchasing.Active ... 5, Purchasing.Paused];
}

// Intentionally out of product order: the reference owns the relationship.
export record table Stock {
    key reference productId: int -> Products.id = [103, 100, 105, 102, 104, 101];
    onHand: Units = [5, 4, 0, 2, 8, 10];
}

// Compute the purchasing policy once per product, then reuse it for quantity and cost.
export record table PurchasePlan = derive Products as product
    join Stock as stock through stock.productId
    join Suppliers as supplier through product.supplierId {
    const orderUnits: Units = match product.purchasing {
        Active => if stock.onHand < product.reorderPoint && stock.onHand < product.targetStock {
            product.targetStock - stock.onHand
        } else { 0 },
        Paused => 0,
    };
    productId: int = product.id;
    productName: string = product.name;
    supplierId: int = product.supplierId;
    supplierName: string = supplier.name;
    leadDays: int = supplier.leadDays;
    onHand: Units = stock.onHand;
    orderUnits: Units = orderUnits;
    orderCents: Cents = orderUnits * product.costCents;
}

record PurchaseLine {
    productId: int;
    productName: string;
    supplierName: string;
    units: Units;
    costCents: Cents;
}

export record PurchaseTotals {
    totalCents: Cents;
    units: Units;
    lines: int;
}

export record SupplierSummary {
    supplierId: int;
    supplierName: string;
    totalCents: Cents;
    units: Units;
    lines: int;
}

type PurchasePredicate = (line: PurchasePlan.Row) => boolean;
type HasOrderCost = { orderCents: Cents; };

function costOf<T extends HasOrderCost>(line: T): Cents {
    return line.orderCents;
}

function purchaseLine(line: PurchasePlan.Row): PurchaseLine {
    return {
        productId: line.productId,
        productName: line.productName,
        supplierName: line.supplierName,
        units: line.orderUnits,
        costCents: costOf<PurchasePlan.Row>(line)
    };
}

function orderLines(): PurchaseLine[] {
    return PurchasePlan.rows()
        .where(line => line.orderUnits > 0)
        .select(purchaseLine);
}

// Budget means a per-line ceiling, not a total-spend optimizer.
function supplierLines(supplierId: int, maxLineCents: Cents): PurchaseLine[] {
    const eligible: PurchasePredicate = capture { supplierId, maxLineCents } (line: PurchasePlan.Row) =>
        line.supplierId == supplierId && line.orderUnits > 0 && line.orderCents <= maxLineCents;
    return PurchasePlan.rows().where(eligible).select(purchaseLine);
}

function totalCents(): Cents {
    return purchaseTotals().totalCents;
}

function totalUnits(): Units {
    return purchaseTotals().units;
}

function purchaseTotals(): PurchaseTotals {
    return purchaseTotalsFor(PurchasePlan);
}

export function purchaseTotalsFor(plan: PurchasePlan): PurchaseTotals {
    return plan.rows()
        .where(line => line.orderUnits > 0)
        .summarize<PurchaseTotals>(line => ({
            totalCents: sum(line.orderCents),
            units: sum(line.orderUnits),
            lines: count()
        }));
}

function supplierSummaries(): SupplierSummary[] {
    return supplierSummariesFor(PurchasePlan);
}

export function supplierSummariesFor(plan: PurchasePlan): SupplierSummary[] {
    return plan.rows()
        .where(line => line.orderUnits > 0)
        .groupBy(line => line.supplierId, line => line.supplierName)
        .summarize<SupplierSummary>(line => ({
            supplierId: line.supplierId,
            supplierName: line.supplierName,
            totalCents: sum(line.orderCents),
            units: sum(line.orderUnits),
            lines: count()
        }));
}

function workbookPlan(): PurchasePlan {
    return PurchasePlan;
}

function previewStock(): Stock {
    return Stock with { onHand: [5, 12, 0, 2, 8, 10] };
}

function previewPlan(stock: Stock): PurchasePlan {
    return PurchasePlan.recalculate({ Stock: stock });
}

function previewTotals(stock: Stock): PurchaseTotals {
    return purchaseTotalsFor(previewPlan(stock));
}

function previewSupplierSummaries(stock: Stock): SupplierSummary[] {
    return supplierSummariesFor(previewPlan(stock));
}

function workbookStock(): Stock {
    return Stock;
}
