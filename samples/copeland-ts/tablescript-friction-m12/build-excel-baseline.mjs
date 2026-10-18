import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import assert from "node:assert/strict";
import { SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const sampleDirectory = path.dirname(fileURLToPath(import.meta.url));
const repository = path.resolve(sampleDirectory, "../../..");
const evidence = path.resolve(repository, process.argv[2] ?? "artifacts/local/tablescript-cafe");
const outputDirectory = path.resolve(repository, process.argv[3] ?? "artifacts/local/tablescript-cafe/excel");
await fs.mkdir(outputDirectory, { recursive: true });

async function readRows(name) {
    const result = JSON.parse(await fs.readFile(path.join(evidence, `${name}.json`), "utf8"));
    assert.equal(result.success, true);
    assert.equal(result.executor, "csharp-relation-plan");
    return result.rows;
}

const products = await readRows("products");
const stock = await readRows("stock");
const suppliers = await readRows("source-suppliers");
const copelandPlan = await readRows("plan");
const editedPlan = await readRows("changed-plan");
const supplierTotals = await readRows("suppliers");

const workbook = Workbook.create();
const restock = workbook.worksheets.add("Restock");
const inputs = workbook.worksheets.add("Inputs");

function formatSheet(sheet, lastColumn, lastRow) {
    sheet.showGridLines = false;
    const area = sheet.getRange(`A1:${lastColumn}${lastRow}`);
    area.format.font = { name: "Arial", size: 10, color: "#233444" };
    area.format.rowHeight = 24;
    area.format.verticalAlignment = "center";
    area.format.columnWidth = 11;
    sheet.freezePanes.freezeRows(sheet.name === "Restock" ? 12 : 5);
}

function heading(sheet, address, text) {
    const range = sheet.getRange(address);
    range.merge();
    range.values = [[text]];
    range.format.font = { name: "Arial", size: 15, bold: true, color: "#173D48" };
    range.format.rowHeight = 30;
}

function header(sheet, address) {
    sheet.getRange(address).format = {
        fill: "#173D48",
        font: { name: "Arial", size: 10, bold: true, color: "#FFFFFF" },
        rowHeight: 30,
        wrapText: true
    };
}

formatSheet(restock, "J", 20);
formatSheet(inputs, "G", 24);
heading(restock, "A1:J1", "Café restocking · Excel baseline");
restock.getRange("A2:J2").merge();
restock.getRange("A2").values = [["Synthetic data · edit blue inputs on Inputs · dollars displayed from integer cents"]];
restock.getRange("A2:J2").format.font.color = "#687887";
restock.getRange("A10:J10").merge();
restock.getRange("A10").values = [["Order active products below the reorder point up to target stock; paused products stay at zero."]];
restock.getRange("A10:J10").format.font.color = "#687887";

heading(inputs, "A1:G1", "Café inputs");
inputs.getRange("A2:G2").merge();
inputs.getRange("A2").values = [["Source: synthetic Workbook.ts inputs, read through the Copeland table CLI."]];
inputs.getRange("A3:G3").merge();
inputs.getRange("A3").values = [["Keys link the tables. Stock rows deliberately use a different product order."]];
inputs.getRange("A2:G3").format.font.color = "#687887";

inputs.getRange("A5:G5").values = [["Product ID", "Product", "Supplier ID", "Cost cents", "Reorder", "Target", "Policy"]];
inputs.getRange("A6:G11").values = products.map(product => [
    product.id, product.name, product.supplierId, product.costCents,
    product.reorderPoint, product.targetStock, product.purchasing
]);
inputs.getRange("A16:B16").values = [["Product ID", "On hand"]];
inputs.getRange("A17:B22").values = stock.map(item => [item.productId, item.onHand]);
inputs.getRange("D16:F16").values = [["Supplier ID", "Supplier", "Lead days"]];
inputs.getRange("D17:F19").values = suppliers.map(supplier => [supplier.id, supplier.name, supplier.leadDays]);
inputs.tables.add("A5:G11", true, "CatalogInputs");
inputs.tables.add("A16:B22", true, "StockInputs");
inputs.tables.add("D16:F19", true, "SupplierInputs");
for (const address of ["A5:G5", "A16:B16", "D16:F16"]) {
    header(inputs, address);
}
for (const address of ["A6:G11", "A17:B22", "D17:F19"]) {
    inputs.getRange(address).format.font.color = "#245BB2";
    inputs.getRange(address).format.fill = "#F0F5FA";
}
inputs.getRange("B:B").format.columnWidth = 23;
inputs.getRange("E:E").format.columnWidth = 23;
inputs.getRange("A6:A11").dataValidation = { rule: { type: "whole", operator: "between", formula1: 1, formula2: 1000000 } };
inputs.getRange("D6:F11").dataValidation = { rule: { type: "whole", operator: "between", formula1: 0, formula2: 1000000 } };
inputs.getRange("B17:B22").dataValidation = { rule: { type: "whole", operator: "between", formula1: 0, formula2: 1000000 } };
inputs.getRange("G6:G11").dataValidation = { rule: { type: "list", values: ["Active", "Paused"] } };
inputs.getRange("G6:G11").format.horizontalAlignment = "center";

restock.getRange("A12:J12").values = [["Product ID", "Product", "Supplier", "Stock", "Reorder", "Target", "Policy", "Unit $", "Order units", "Order $"]];
const formulas = [];
for (let index = 0; index < products.length; index += 1) {
    const row = 13 + index;
    const inputRow = 6 + index;
    formulas.push([
        `=Inputs!A${inputRow}`,
        `=Inputs!B${inputRow}`,
        `=INDEX(Inputs!$E$17:$E$19,MATCH(Inputs!C${inputRow},Inputs!$D$17:$D$19,0))`,
        `=INDEX(Inputs!$B$17:$B$22,MATCH(A${row},Inputs!$A$17:$A$22,0))`,
        `=Inputs!E${inputRow}`,
        `=Inputs!F${inputRow}`,
        `=Inputs!G${inputRow}`,
        `=Inputs!D${inputRow}/100`,
        `=IF(G${row}="Paused",0,IF(AND(D${row}<E${row},D${row}<F${row}),F${row}-D${row},0))`,
        `=I${row}*H${row}`
    ]);
}
restock.getRange("A13:J18").formulas = formulas;
restock.tables.add("A12:J18", true, "OrderCalculation");
header(restock, "A12:J12");
restock.getRange("H13:H18").setNumberFormat("$0.00");
restock.getRange("J13:J18").setNumberFormat("$#,##0.00");
restock.getRange("G13:G18").format.horizontalAlignment = "center";
restock.getRange("A13:J18").conditionalFormats.addCustom("=$I13>0", { fill: "#E5F2ED" });
restock.getRange("A:A").format.columnWidth = 10;
restock.getRange("B:B").format.columnWidth = 23;
restock.getRange("C:C").format.columnWidth = 21;
restock.getRange("D:J").format.columnWidth = 12;

restock.getRange("A4:E4").values = [["Supplier ID", "Supplier", "Units", "Lines", "Spend"]];
const summaryFormulas = suppliers.map((supplier, index) => {
    const row = 5 + index;
    const inputRow = 17 + index;
    return [
        `=Inputs!D${inputRow}`, `=Inputs!E${inputRow}`,
        `=SUMIF(Inputs!$C$6:$C$11,A${row},$I$13:$I$18)`,
        `=COUNTIFS(Inputs!$C$6:$C$11,A${row},$I$13:$I$18,">0")`,
        `=SUMIF(Inputs!$C$6:$C$11,A${row},$J$13:$J$18)`
    ];
});
restock.getRange("A5:E7").formulas = summaryFormulas;
header(restock, "A4:E4");
restock.getRange("E5:E7").setNumberFormat("$#,##0.00");
restock.getRange("I4:J4").values = [["Purchase plan", "Total"]];
header(restock, "I4:J4");
restock.getRange("I5:I7").values = [["Spend"], ["Units"], ["Order lines"]];
restock.getRange("J5:J7").formulas = [["=SUM(J13:J18)"], ["=SUM(I13:I18)"], ["=COUNTIF(I13:I18,\">0\")"]];
restock.getRange("J5").setNumberFormat("$#,##0.00");
restock.getRange("I5:J7").format.fill = "#E5F2ED";
restock.getRange("J5:J7").format.font.bold = true;

workbook.recalculate();

function verifyPlan(expected) {
    const values = restock.getRange("A13:J18").values;
    assert.equal(values.length, expected.length);
    for (let index = 0; index < expected.length; index += 1) {
        const line = expected[index];
        assert.equal(values[index][0], line.productId);
        assert.equal(values[index][1], line.productName);
        assert.equal(values[index][2], line.supplierName);
        assert.equal(values[index][3], line.onHand);
        assert.equal(values[index][8], line.orderUnits);
        assert.equal(Math.round(values[index][9] * 100), line.orderCents);
    }
}

verifyPlan(copelandPlan);
assert.deepEqual(restock.getRange("J5:J7").values, [[312.1], [67], [3]]);
const summaryValues = restock.getRange("A5:E7").values;
for (let index = 0; index < supplierTotals.length; index += 1) {
    assert.equal(summaryValues[index][0], supplierTotals[index].supplierId);
    assert.equal(summaryValues[index][2], supplierTotals[index].units);
    assert.equal(summaryValues[index][3], supplierTotals[index].lines);
    assert.equal(Math.round(summaryValues[index][4] * 100), supplierTotals[index].totalCents);
}

// Independent arithmetic control, before comparing engines.
assert.equal(20 * 925 + 16 * 310 + 31 * 250, 31210);
inputs.getRange("B18").values = [[12]];
workbook.recalculate();
verifyPlan(editedPlan);
assert.deepEqual(restock.getRange("J5:J7").values, [[127.1], [47], [2]]);
inputs.getRange("B18").values = [[4]];
workbook.recalculate();
verifyPlan(copelandPlan);

for (const sheet of [restock, inputs]) {
    const values = sheet.getUsedRange().values;
    for (const row of values) {
        for (const value of row) {
            assert.equal(typeof value === "string" && /^#(REF!|DIV\/0!|VALUE!|N\/A|NAME\?|NUM!|SPILL!|CALC!)/.test(value), false);
        }
    }
    const preview = await workbook.render({ sheetName: sheet.name, autoCrop: "all", scale: 1.5, format: "png" });
    await fs.writeFile(path.join(outputDirectory, `${sheet.name.toLowerCase()}-preview.png`), new Uint8Array(await preview.arrayBuffer()));
}

const inspection = await workbook.inspect({ kind: "region", sheetId: "Restock", range: "A13:J18", maxChars: 4000 });
await fs.writeFile(path.join(evidence, "excel-inspection.json"), JSON.stringify(inspection, null, 2));
const xlsx = await SpreadsheetFile.exportXlsx(workbook);
const outputPath = path.join(outputDirectory, "CafeRestocking-Excel.xlsx");
await xlsx.save(outputPath);
await fs.writeFile(path.join(evidence, "excel-validation.json"), JSON.stringify({
    success: true,
    baselineCents: 31210,
    editedCents: 12710,
    rowChecks: "IDs, names, suppliers, stock, units, cents for all six rows before and after edit",
    supplierChecks: 3,
    restored: true,
    formulaErrors: 0,
    nativeExcel: "not exercised",
    calculationEngine: "artifact-tool",
    outputPath
}, null, 2));
console.log("TABLESCRIPT_M12_EXCEL_PASSED baseline=31210 edited=12710 restored=true");
