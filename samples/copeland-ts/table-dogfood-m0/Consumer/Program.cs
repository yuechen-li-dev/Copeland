using WorkbookData = Copeland.TableDogfoodM0.Copeland.Workbook;

var products = WorkbookData.workbookProducts();
var prices = WorkbookData.workbookPrices();
var margins = WorkbookData.workbookMargins();
var catalog = WorkbookData.workbookCatalog();
var inventoryCatalog = WorkbookData.workbookInventoryCatalog();
var revisedPrices = WorkbookData.revisedPrices();
var activeProducts = WorkbookData.activeProducts(products);

Console.WriteLine($"sheets=Categories,Products,Prices,PriceMargins,Inventory,ProductCatalog,InventoryCatalog");
Console.WriteLine($"product-rows={products.RowCount}");
Console.WriteLine($"active-products={activeProducts.Length}");
Console.WriteLine($"retail=sum:{WorkbookData.retailSum():F2},count:{WorkbookData.retailCount()},average:{WorkbookData.retailAverage():F2},min:{WorkbookData.retailMinimum():F2},max:{WorkbookData.retailMaximum():F2}");
Console.WriteLine($"margin=sum:{WorkbookData.marginSum():F2},first:{margins.productId.At(0).Value}:{margins.margin.At(0).Value:F2}");
Console.WriteLine($"catalog={catalog.productId.At(1).Value}:{catalog.productName.At(1).Value}:{catalog.categoryName.At(1).Value}:{catalog.retail.At(1).Value:F2}");
Console.WriteLine($"inventory-value={WorkbookData.inventoryValue():F2}");
Console.WriteLine($"inventory-catalog={inventoryCatalog.productId.At(1).Value}:{inventoryCatalog.inventoryValue.At(1).Value:F2}");
Console.WriteLine($"state={WorkbookData.stateLabel(catalog.state.At(1).Value)}");
Console.WriteLine($"original-retail={prices.productId.At(1).Value}:{prices.retail.At(1).Value:F2}");
Console.WriteLine($"revised-retail={revisedPrices.productId.At(1).Value}:{revisedPrices.retail.At(1).Value:F2}");

Require(products.RowCount == 5 && activeProducts.Length == 3, "Product query");
Require(WorkbookData.retailSum() == 115.0 && WorkbookData.retailCount() == 5, "Retail summary");
Require(WorkbookData.retailAverage() == 23.0 && WorkbookData.retailMinimum() == 8.75 && WorkbookData.retailMaximum() == 42.0, "Retail statistics");
Require(catalog.categoryName.At(1).Value == "Coffee" && catalog.productName.At(1).Value == "Filter Beans", "Declared-reference catalog join");
Require(WorkbookData.inventoryValue() == 969.25 && inventoryCatalog.inventoryValue.At(1).Value == 65.0, "Chained inventory join");
Require(WorkbookData.stateLabel(catalog.state.At(1).Value) == "low-stock", "Enum projection");
Require(prices.retail.At(1).Value == 16.25 && revisedPrices.retail.At(1).Value == 16.50, "Immutable price revision");
Console.WriteLine("Catalog assertions passed.");

static void Require(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException(description);
    }
}
