using System.Text.Json;
using Workbook = Copeland.Generated.CopelandModule;

var original = Workbook.workbookPlan();
var stock = Workbook.previewStock();
var before = Project(
    Workbook.scenarioPlanValues(original), Workbook.scenarioPlanNames(original),
    Workbook.projectTotals(Workbook.purchaseTotals()),
    Workbook.projectSupplierValues(Workbook.supplierSummaries()),
    Workbook.projectSupplierNames(Workbook.supplierSummaries()));
var revised = Workbook.previewPlan(stock);
var preview = Project(
    Workbook.scenarioPlanValues(revised), Workbook.scenarioPlanNames(revised),
    Workbook.projectTotals(Workbook.previewTotals(stock)),
    Workbook.projectSupplierValues(Workbook.previewSupplierSummaries(stock)),
    Workbook.projectSupplierNames(Workbook.previewSupplierSummaries(stock)));
var again = Workbook.previewPlan(stock);
var repeated = Project(
    Workbook.scenarioPlanValues(again), Workbook.scenarioPlanNames(again),
    Workbook.projectTotals(Workbook.purchaseTotalsFor(again)),
    Workbook.projectSupplierValues(Workbook.supplierSummariesFor(again)),
    Workbook.projectSupplierNames(Workbook.supplierSummariesFor(again)));
var after = Project(
    Workbook.scenarioPlanValues(Workbook.workbookPlan()), Workbook.scenarioPlanNames(Workbook.workbookPlan()),
    Workbook.projectTotals(Workbook.purchaseTotals()),
    Workbook.projectSupplierValues(Workbook.supplierSummaries()),
    Workbook.projectSupplierNames(Workbook.supplierSummaries()));

Console.WriteLine(JsonSerializer.Serialize(new { before, preview, repeated, after }));

static object Project(int[] rowValues, string[] rowNames, int[] totals, int[] values, string[] names)
{
    var rows = Enumerable.Range(0, rowNames.Length / 2).Select(index => new
    {
        productId = rowValues[index * 6],
        productName = rowNames[index * 2],
        supplierId = rowValues[index * 6 + 1],
        supplierName = rowNames[index * 2 + 1],
        leadDays = rowValues[index * 6 + 2],
        onHand = rowValues[index * 6 + 3],
        orderUnits = rowValues[index * 6 + 4],
        orderCents = rowValues[index * 6 + 5]
    }).ToArray();
    var suppliers = Enumerable.Range(0, names.Length).Select(index => new
    {
        supplierId = values[index * 4],
        supplierName = names[index],
        totalCents = values[index * 4 + 1],
        units = values[index * 4 + 2],
        lines = values[index * 4 + 3]
    }).ToArray();
    return new
    {
        rows,
        totals = new { totalCents = totals[0], units = totals[1], lines = totals[2] },
        suppliers
    };
}
