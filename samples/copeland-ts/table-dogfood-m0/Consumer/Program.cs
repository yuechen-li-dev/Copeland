using WorkbookData = Copeland.TableDogfoodM0.Copeland.Workbook;

var products = WorkbookData.workbookProducts();
var prices = WorkbookData.workbookPrices();
var revisedPrices = WorkbookData.revisedPrices();
var activeProducts = WorkbookData.activeProducts(products);

Console.WriteLine($"sheets=Categories,Products,Prices,Inventory");
Console.WriteLine($"product-rows={products.RowCount}");
Console.WriteLine($"active-products={activeProducts.Length}");
Console.WriteLine($"retail=sum:{WorkbookData.retailSum():F2},count:{WorkbookData.retailCount()},average:{WorkbookData.retailAverage():F2},min:{WorkbookData.retailMinimum():F2},max:{WorkbookData.retailMaximum():F2}");
Console.WriteLine($"lookup={WorkbookData.categoryNameFor(101)}");
Console.WriteLine($"inventory-value={WorkbookData.inventoryValue():F2}");
Console.WriteLine($"state={WorkbookData.stateLabel(101)}");
Console.WriteLine($"original-retail={prices.productId.At(1).Value}:{prices.retail.At(1).Value:F2}");
Console.WriteLine($"revised-retail={revisedPrices.productId.At(1).Value}:{revisedPrices.retail.At(1).Value:F2}");
