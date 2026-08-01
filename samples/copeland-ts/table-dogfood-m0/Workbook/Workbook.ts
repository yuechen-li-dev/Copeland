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

// M1B: joins consume declared references and preserve Products row order.
export record table ProductCatalog = derive Products as product
    join Categories as category through product.categoryId
    join Prices as price through price.productId {
    productId: int = product.id;
    productName: string = product.name;
    categoryName: string = category.name;
    state: StockState = product.state;
    retail: number = price.retail;
    cost: number = price.cost;
    margin: number = price.retail - price.cost;
}

export record table InventoryCatalog = derive Inventory as inventory
    join Products as product through inventory.productId
    join Prices as price through price.productId {
    productId: int = product.id;
    productName: string = product.name;
    onHand: int = inventory.onHand;
    retail: number = price.retail;
    inventoryValue: number = Float.From(inventory.onHand) * price.retail;
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

function workbookCatalog(): ProductCatalog {
    return ProductCatalog;
}

function workbookInventoryCatalog(): InventoryCatalog {
    return InventoryCatalog;
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

function inventoryValue(): number {
    return InventoryCatalog.inventoryValue.sum();
}

function stateLabel(state: StockState): string {
    return match state {
        Active => "active",
        LowStock => "low-stock",
        Discontinued => "discontinued",
    };
}
