export type CopelandProfile = "Copeland";

import { Products, Stock, Suppliers, PurchasePlan, PurchaseTotals, SupplierSummary,
    purchaseTotalsFor, supplierSummariesFor } from "./Workbook";

// Application inputs and calculated views are immutable values. No UI dependency.
export record WorkbookInputs {
    products: Products;
    stock: Stock;
    suppliers: Suppliers;
}

export record ProductLine {
    productId: int;
    productName: string;
    supplierName: string;
    onHand: int;
    orderUnits: int;
    orderCents: int;
}

export record WorkbookView {
    inputs: WorkbookInputs;
    plan: PurchasePlan;
    products: ProductLine[];
    totals: PurchaseTotals;
    suppliers: SupplierSummary[];
}

export enum StockError {
    NegativeCount(productId: int, units: int),
    UnknownProduct(productId: int),
}

function productLine(row: PurchasePlan.Row): ProductLine {
    return {
        productId: row.productId,
        productName: row.productName,
        supplierName: row.supplierName,
        onHand: row.onHand,
        orderUnits: row.orderUnits,
        orderCents: row.orderCents,
    };
}

export function calculate(inputs: WorkbookInputs): WorkbookView {
    const plan: PurchasePlan = PurchasePlan.recalculate({
        Products: inputs.products,
        Stock: inputs.stock,
        Suppliers: inputs.suppliers,
    });
    return {
        inputs,
        plan,
        products: plan.rows().select(productLine),
        totals: purchaseTotalsFor(plan),
        suppliers: supplierSummariesFor(plan),
    };
}

export function initialWorkbook(): WorkbookView {
    return calculate({ products: Products, stock: Stock, suppliers: Suppliers });
}

export function setOnHand(stock: Stock, productId: int, units: int): Stock ! StockError {
    if (units < 0) {
        return err(StockError.NegativeCount(productId, units));
    }
    return match stock.withRow({ productId }, { onHand: units }) {
        ok(updated) => ok(updated),
        err(error) => err(StockError.UnknownProduct(productId)),
    };
}

// A local scenario request. Persistence would revalidate against server-owned inputs.
export function applyStockEdit(view: WorkbookView, productId: int, units: int): WorkbookView ! StockError {
    return match setOnHand(view.inputs.stock, productId, units) {
        ok(stock) => ok(calculate(view.inputs with { stock })),
        err(error) => err(error),
    };
}

export function baselineCents(): int {
    return purchaseTotalsFor(PurchasePlan).totalCents;
}

// The same application path is executed by the C# and browser qualification.
export function applicationProbe(): int[] {
    const original: WorkbookView = initialWorkbook();
    const revised: WorkbookView = applyStockEdit(original, 100, 12)!;
    const negative: int = match applyStockEdit(revised, 100, -1) {
        ok(value) => 0,
        err(error) => match error {
            NegativeCount(productId, units) => if productId == 100 && units == -1 { 1 } else { 0 },
            UnknownProduct(productId) => 0,
        },
    };
    const missing: int = match applyStockEdit(revised, 999, 2) {
        ok(value) => 0,
        err(error) => match error {
            NegativeCount(productId, units) => 0,
            UnknownProduct(productId) => if productId == 999 { 1 } else { 0 },
        },
    };
    return [original.totals.totalCents, revised.totals.totalCents, revised.totals.units,
        revised.totals.lines, revised.products.length, revised.products[0].onHand,
        revised.products[3].onHand, revised.suppliers.length, revised.suppliers[0].supplierId,
        revised.suppliers[1].supplierId, negative, missing, baselineCents()];
}
