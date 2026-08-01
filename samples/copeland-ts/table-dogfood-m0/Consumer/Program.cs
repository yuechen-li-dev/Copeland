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
