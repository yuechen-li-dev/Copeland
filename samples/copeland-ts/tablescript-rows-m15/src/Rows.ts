export type CopelandProfile = "Copeland";

import { Products, Stock, Suppliers, PurchasePlan, Purchasing } from "./Workbook";
import { WorkbookInputs, WorkbookView, initialWorkbook, viewFromPlan } from "./Application";
import { PriceError, validatePrice } from "./PriceApplication";

export record NewProduct {
    productId: int;
    name: string;
    supplierId: int;
    costCents: int;
    onHand: int;
}

export enum RowError {
    InvalidId,
    EmptyName,
    NegativeStock,
    TotalTooLarge,
    Price(error: PriceError),
    Integrity(error: TableEditError),
    Calculation(error: TableRelationError),
}

function calculateCandidate(inputs: WorkbookInputs): WorkbookView ! RowError {
    return match PurchasePlan.tryRecalculate({ Products: inputs.products, Stock: inputs.stock, Suppliers: inputs.suppliers }) {
        ok(plan) => validateView(viewFromPlan(inputs, plan)),
        err(error) => err(RowError.Calculation(error)),
    };
}

// Individual row costs are bounded by this application's price/stock policies.
// Summary int arithmetic wraps; the application refuses an unrepresentable total.
export function validateView(view: WorkbookView): WorkbookView ! RowError {
    let total: int = 0;
    for (const line of view.products) {
        if (line.orderCents < 0 || total > 2147483647 - line.orderCents) {
            return err(RowError.TotalTooLarge);
        }
        total = total + line.orderCents;
    }
    return ok(view);
}

export function createProduct(view: WorkbookView, request: NewProduct): WorkbookView ! RowError {
    if (request.productId <= 0) {
        return err(RowError.InvalidId);
    }
    if (request.name.length == 0) {
        return err(RowError.EmptyName);
    }
    if (request.onHand < 0) {
        return err(RowError.NegativeStock);
    }
    const price: int ! RowError = match validatePrice(request.productId, request.costCents) {
        ok(cents) => ok(cents), err(error) => err(RowError.Price(error)),
    };
    const costCents: int = price?;
    const candidate: WorkbookInputs ! TableEditError = Tables.apply(view.inputs, {
        products: [Products.insertRow({
            id: request.productId, supplierId: request.supplierId, name: request.name,
            costCents, reorderPoint: 8, targetStock: 24, purchasing: Purchasing.Active,
        })],
        stock: [Stock.insertRow({ productId: request.productId, onHand: request.onHand })],
    });
    return match candidate {
        ok(inputs) => calculateCandidate(inputs),
        err(error) => err(RowError.Integrity(error)),
    };
}

export function removeProduct(view: WorkbookView, productId: int): WorkbookView ! RowError {
    return match Tables.apply(view.inputs, {
        products: [Products.deleteRow({ id: productId })],
        stock: [Stock.deleteRow({ productId })],
    }) {
        ok(inputs) => calculateCandidate(inputs), err(error) => err(RowError.Integrity(error)),
    };
}

export function removeSupplier(view: WorkbookView, supplierId: int): WorkbookView ! RowError {
    return match Tables.apply(view.inputs, { suppliers: [Suppliers.deleteRow({ id: supplierId })] }) {
        ok(inputs) => calculateCandidate(inputs), err(error) => err(RowError.Integrity(error)),
    };
}

export function rowsProbe(): int[] {
    const original: WorkbookView = initialWorkbook();
    const request: NewProduct = { productId: 106, name: "House chocolate", supplierId: 30, costCents: 500, onHand: 2 };
    const added: WorkbookView = createProduct(original, request)!;
    const duplicate: int = match createProduct(added, request) { ok(view) => 0, err(error) => 1, };
    const dangling: int = match createProduct(original, request with { supplierId: 999 }) { ok(view) => 0, err(error) => 1, };
    const supplier: int = match removeSupplier(original, 10) { ok(view) => 0, err(error) => 1, };
    const removed: WorkbookView = removeProduct(added, 106)!;
    let empty: WorkbookView = original;
    for (const line of original.products) {
        empty = removeProduct(empty, line.productId)!;
    }
    const restored: WorkbookView = createProduct(empty, request)!;
    return [original.products.length, added.products.length, added.totals.totalCents, duplicate, dangling, supplier,
        removed.products.length, removed.totals.totalCents, empty.products.length, empty.totals.totalCents,
        empty.suppliers.length, restored.products.length, restored.totals.totalCents, original.totals.totalCents];
}

export function limitWorkbook(): WorkbookView {
    let view: WorkbookView = initialWorkbook();
    for (let index: int = 0; index < 89; index = index + 1) {
        view = createProduct(view, {
            productId: 1000 + index, name: "Limit specimen", supplierId: 30, costCents: 1000000, onHand: 0,
        })!;
    }
    return view;
}

export function totalLimitProbe(): int[] {
    const view: WorkbookView = limitWorkbook();
    const refused: int = match createProduct(view, {
        productId: 2000, name: "Overflow specimen", supplierId: 30, costCents: 1000000, onHand: 0,
    }) {
        ok(accepted) => 0,
        err(error) => match error {
            TotalTooLarge => 1, InvalidId => 0, EmptyName => 0, NegativeStock => 0,
            Price(priceError) => 0, Integrity(tableError) => 0, Calculation(relationError) => 0,
        },
    };
    return [view.products.length, view.totals.totalCents, refused];
}
