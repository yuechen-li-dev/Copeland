using Copeland.TS.Mir;

namespace Copeland.TS.Backend.CSharp;

public static partial class CSharpBackend
{
    private static string EmitTableWithRow(
        CSharpTextWriter writer,
        MirTableWithRowExpression update,
        MirFunction function,
        IReadOnlySet<string> enumNames,
        ref int tempIndex,
        List<CSharpDiagnostic> diagnostics)
    {
        string prefix = $"__cope_row_edit_{tempIndex++}";
        string source = EmitExpression(writer, update.Source, function, enumNames, ref tempIndex, diagnostics);
        writer.WriteLine($"var {prefix}_source = {source};");
        string key = EmitExpression(writer, update.Key, function, enumNames, ref tempIndex, diagnostics);
        writer.WriteLine($"var {prefix}_key = {key};");
        for (int index = 0; index < update.Replacements.Count; index++)
        {
            string value = EmitExpression(writer, update.Replacements[index].Value, function, enumNames, ref tempIndex, diagnostics);
            writer.WriteLine($"var {prefix}_value_{index} = {value};");
        }
        writer.WriteLine($"int {prefix}_index = {prefix}_source.FindKey({prefix}_key);");
        string resultType = MapType(update.ResultType);
        writer.WriteLine($"{resultType} {prefix}_result;");
        writer.WriteLine($"if ({prefix}_index < 0)");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"{prefix}_result = {resultType}.Err(new TableKeyError.NotFound());");
        writer.Unindent();
        writer.WriteLine("}");
        writer.WriteLine("else");
        writer.WriteLine("{");
        writer.Indent();
        writer.WriteLine($"var {prefix}_updated = {prefix}_source;");
        for (int index = 0; index < update.Replacements.Count; index++)
        {
            string method = TableWithColumnMethodName(update.Replacements[index].ColumnId) + "Cell";
            writer.WriteLine($"{prefix}_updated = {prefix}_updated.{method}({prefix}_index, {prefix}_value_{index});");
        }
        writer.WriteLine($"{prefix}_result = {resultType}.Ok({prefix}_updated);");
        writer.Unindent();
        writer.WriteLine("}");
        return prefix + "_result";
    }
}
