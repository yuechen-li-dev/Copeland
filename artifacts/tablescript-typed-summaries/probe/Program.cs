using System.Text.Json;
using Workbook = Copeland.Generated.CopelandModule;

var totals = Workbook.typedTotals();
var values = Workbook.typedSupplierValues();
var names = Workbook.typedSupplierNames();
var suppliers = Enumerable.Range(0, names.Length).Select(index => new
{
    supplierId = values[index * 4],
    supplierName = names[index],
    totalCents = values[index * 4 + 1],
    units = values[index * 4 + 2],
    lines = values[index * 4 + 3]
}).ToArray();

Console.WriteLine(JsonSerializer.Serialize(new
{
    totals = new { totalCents = totals[0], units = totals[1], lines = totals[2] },
    suppliers
}));
