using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Tson;

namespace TinyFarm.Core;

public static class TinyFarmDefinitionLoader
{
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Content", "tiny-farm-definitions.obj.ts");

    public static TinyFarmDefinitions Load(string? path = null)
    {
        string source = File.ReadAllText(path ?? DefaultPath);
        TsonReadResult read = TsonDocumentReader.ReadSelfDescribed(source, TsonDocumentProfile.ObjectTypeScript);
        if (!read.Success || read.Document?.Root is not TsonTable table)
        {
            IEnumerable<string> diagnosticsItems = read.SyntaxDiagnostics
                .Select(item => item.ToString())
                .Concat(read.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
            string diagnostics = string.Join("; ", diagnosticsItems);
            throw new InvalidDataException($"TinyFarm TSON definitions are invalid: {diagnostics}");
        }

        string identity = "tiny-farm-content-m2-sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        var items = new List<ItemDefinition>();
        var crops = new List<CropDefinition>();
        for (int row = 0; row < table.RowCount; row++)
        {
            ProductId productId = new(Text(table, "id", row));
            items.Add(new ItemDefinition(productId, Text(table, "name", row), Number(table, "buyPrice", row), Number(table, "sellPrice", row)));
            string cropId = Text(table, "cropId", row);
            if (cropId.Length > 0)
            {
                crops.Add(new CropDefinition(
                    new CropId(cropId),
                    new ProductId(Text(table, "seedItemId", row)),
                    new ProductId(Text(table, "harvestItemId", row)),
                    Number(table, "growthDays", row),
                    Number(table, "waterRequirement", row),
                    Number(table, "yieldCount", row)));
            }
        }

        return new TinyFarmDefinitions(identity, items, crops);
    }

    private static TsonValue Cell(TsonTable table, string column, int row)
    {
        return table.Columns.Single(item => item.Schema.Name == column).Cells[row];
    }

    private static string Text(TsonTable table, string column, int row) => ((TsonString)Cell(table, column, row)).Value;
    private static int Number(TsonTable table, string column, int row) => checked((int)((TsonNumber)Cell(table, column, row)).Value);
}
