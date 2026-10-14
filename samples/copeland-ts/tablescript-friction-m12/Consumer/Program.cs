using WorkbookData = Copeland.TableScriptFrictionM12.Copeland.Workbook;

var plan = WorkbookData.workbookPlan();
var orders = WorkbookData.orderLines();
var supplierOrders = WorkbookData.supplierLines(10, 20000);
var tooExpensive = WorkbookData.supplierLines(10, 18499);
var stock = WorkbookData.workbookStock();
var preview = WorkbookData.previewStock();
var totals = WorkbookData.purchaseTotals();
var suppliers = WorkbookData.supplierSummaries();
var previewPlan = WorkbookData.previewPlan(preview);
var previewTotals = WorkbookData.previewTotals(preview);
var previewSuppliers = WorkbookData.previewSupplierSummaries(preview);

Require(plan.RowCount == 6, "all product rows survive keyed joins");
Require(plan.productId.At(0).Value == 100, "source product order survives shuffled stock");
Require(plan.onHand.At(0).Value == 4, "stock joins by key");
Require(plan.orderUnits.At(4).Value == 0, "exact reorder threshold does not order");
Require(plan.orderUnits.At(5).Value == 0, "paused product does not order");
Require(orders.Length == 3, "three actionable lines");
Require(WorkbookData.totalUnits() == 67, "total units");
Require(WorkbookData.totalCents() == 31210, "total integer cents");
Require(supplierOrders.Length == 1, "captured supplier and per-line ceiling");
Require(tooExpensive.Length == 0, "per-line ceiling excludes expensive order");
Require(stock.onHand.At(1).Value == 4, "with preserves original stock");
Require(preview.onHand.At(1).Value == 12, "with creates a stock snapshot");
Require(WorkbookData.totalCents() == 31210, "snapshot does not silently mutate derived globals");
Require(totals is not null, "typed purchase total reaches the C# consumer");
Require(suppliers.Length == 3, "three typed supplier summaries");
Require(previewPlan.RowCount == 6, "preview preserves the product row shape");
Require(previewPlan.onHand.At(0).Value == 12, "preview uses the passed stock snapshot through its shuffled key");
Require(previewPlan.orderUnits.At(0).Value == 0, "revised espresso stock removes its order");
Require(previewPlan.orderCents.At(2).Value == 4960, "tea purchase is preserved in the preview");
Require(previewPlan.orderCents.At(3).Value == 7750, "pantry purchase is preserved in the preview");
Require(previewTotals is not null && previewSuppliers.Length == 2, "preview supplies typed totals and two supplier summaries");
Require(plan.onHand.At(0).Value == 4 && WorkbookData.totalCents() == 31210, "preview preserves the global plan and totals");

Console.WriteLine($"TABLESCRIPT_M12_PASSED rows={plan.RowCount} lines={orders.Length} units={WorkbookData.totalUnits()} cents={WorkbookData.totalCents()}");

static void Require(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException(description);
    }
}
