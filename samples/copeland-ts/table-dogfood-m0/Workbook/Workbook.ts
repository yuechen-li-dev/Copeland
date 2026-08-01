enum StockState {
    Active,
    LowStock,
    Discontinued,
}

export record table Categories {
    key id: int = [10, 20, 30];
    name: string = ["Coffee", "Tea", "Equipment"];
}

export record table Products {
    key id: int = [100, 101, 102, 103, 104];
    reference categoryId: int -> Categories.id = [10, 10, 20, 30, 30];
    name: string = ["Espresso Beans", "Filter Beans", "Earl Grey", "Kettle", "Scale"];
    state: StockState = [
        StockState.Active,
        StockState.LowStock,
        StockState.Active,
        StockState.Active,
        StockState.Discontinued
    ];
}

export record table Prices {
    key reference productId: int -> Products.id = [100, 101, 102, 103, 104];
    retail: number = [18.50, 16.25, 8.75, 42.00, 29.50];
    cost: number = [9.25, 7.50, 3.10, 21.00, 15.25];
}

// M1A: source-ordered, read-only columnar projection. Cross-table lookup
// remains explicit below; joins are intentionally outside this milestone.
export record table PriceMargins = derive Prices as price {
    productId: int = price.productId;
    retail: number = price.retail;
    margin: number = price.retail - price.cost;
}

export record table Inventory {
    key reference productId: int -> Products.id = [100, 101, 102, 103, 104];
    onHand: int = [24, 4, 19, 7, 0];
    reorderPoint: int = [8, 8, 6, 3, 2];
}

record ProductView {
    name: string;
    state: StockState;
}

function workbookProducts(): Products {
    return Products;
}

function workbookPrices(): Prices {
    return Prices;
}

function workbookMargins(): PriceMargins {
    return PriceMargins;
}

function revisedPrices(): Prices {
    return Prices with {
        retail: [18.50, 16.50, 8.75, 42.00, 29.50]
    };
}

function activeProducts(products: Products): ProductView[] {
    return products.rows()
        .where(row => isActive(row.state))
        .select(productView);
}

function isActive(state: StockState): boolean {
    return match state {
        Active => true,
        LowStock => false,
        Discontinued => false,
    };
}

function productView(row: Products.Row): ProductView {
    return {
        name: row.name,
        state: row.state
    };
}

function retailSum(): number {
    return Prices.retail.sum();
}

function marginSum(): number {
    return PriceMargins.margin.sum();
}

function retailCount(): int {
    return Prices.retail.count();
}

function retailAverage(): number {
    return Prices.retail.average();
}

function retailMinimum(): number {
    return Prices.retail.min();
}

function retailMaximum(): number {
    return Prices.retail.max();
}

function categoryNameFor(productId: int): string {
    for (const product of Products.rows()) {
        if (product.id == productId) {
            for (const category of Categories.rows()) {
                if (category.id == product.categoryId) {
                    return category.name;
                }
            }
        }
    }
    return "Unknown";
}

function retailFor(productId: int): number {
    for (const price of Prices.rows()) {
        if (price.productId == productId) {
            return price.retail;
        }
    }
    return 0.0;
}

function inventoryValue(): number {
    let total: number = 0.0;
    for (const item of Inventory.rows()) {
        total = total + Float.From(item.onHand) * retailFor(item.productId);
    }
    return total;
}

function stateLabel(productId: int): string {
    for (const product of Products.rows()) {
        if (product.id == productId) {
            return match product.state {
                Active => "active",
                LowStock => "low-stock",
                Discontinued => "discontinued",
            };
        }
    }
    return "unknown";
}
