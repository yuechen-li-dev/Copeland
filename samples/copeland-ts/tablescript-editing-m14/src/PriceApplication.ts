export type CopelandProfile = "Copeland";

import { Products } from "./Workbook";
import { WorkbookView, calculate } from "./Application";

export record EditableProduct {
    productId: int;
    productName: string;
    onHand: int;
    costCents: int;
}

export enum PriceError {
    UnknownProduct(productId: int),
    NegativePrice(productId: int, cents: int),
    PriceTooLarge(productId: int, cents: int),
}

export function editableProduct(view: WorkbookView, productId: int): EditableProduct ! PriceError {
    for (const product of view.inputs.products.rows()) {
        if (product.id == productId) {
            for (const line of view.products) {
                if (line.productId == productId) {
                    return ok({ productId, productName: product.name, onHand: line.onHand, costCents: product.costCents });
                }
            }
        }
    }
    return err(PriceError.UnknownProduct(productId));
}

// This café's price policy belongs to the application, never the text parser.
export function setPrice(products: Products, productId: int, cents: int): Products ! PriceError {
    if (cents < 0) {
        return err(PriceError.NegativePrice(productId, cents));
    }
    if (cents > 1000000) {
        return err(PriceError.PriceTooLarge(productId, cents));
    }
    return match products.withRow({ id: productId }, { costCents: cents }) {
        ok(updated) => ok(updated),
        err(error) => err(PriceError.UnknownProduct(productId)),
    };
}

export function applyPriceEdit(view: WorkbookView, productId: int, cents: int): WorkbookView ! PriceError {
    return match setPrice(view.inputs.products, productId, cents) {
        ok(products) => ok(calculate(view.inputs with { products })),
        err(error) => err(error),
    };
}
