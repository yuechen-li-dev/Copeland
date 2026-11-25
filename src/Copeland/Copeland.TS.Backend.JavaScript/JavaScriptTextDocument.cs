using Copeland.TS.Mir;

namespace Copeland.TS.Backend.JavaScript;

public static partial class JavaScriptBackend
{
    private static EmittedExpression EmitTextDocument(
        MirTextDocumentExpression document,
        MirFunction function,
        EnumCatalog catalog,
        ResultCatalog results,
        GeneratedNames names,
        bool flowEnabled)
    {
        var prelude = new List<EmittedLine>();
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (MirTextValueSlot slot in document.Slots)
        {
            EmittedExpression value = EmitExpression(slot.Expression, function, catalog, results, names, flowEnabled);
            string temporary = names.NextTemporary("text_slot").ToString();
            prelude.AddRange(value.Prelude);
            prelude.Add(new EmittedLine($"const {temporary} = {value.Value};", 0));
            slots.Add(slot.SlotId, temporary);
        }
        var parts = new List<string>();
        AppendNode(document.Root);
        return new EmittedExpression(prelude, $"Object.freeze([{string.Join(", ", parts)}])");

        void AppendNode(MirTextNode node)
        {
            string field = node.Kind switch
            {
                "Heading" => "level",
                "List" => "kind",
                "CodeBlock" => "language",
                "Link" => "target",
                _ => "",
            };
            string carrier = field.Length == 0
                ? $"Object.freeze({{ tag: {Quote(node.Kind)} }})"
                : $"Object.freeze({{ tag: {Quote(node.Kind)}, payload: Object.freeze({{ {field}: {Quote(Attribute(node, field))} }}) }})";
            AddPart("Open", $"node: {carrier}, role: {Quote(Attribute(node, "role"))}");
            foreach (MirTextContent child in node.Children)
            {
                switch (child)
                {
                    case MirTextChild nested:
                        AppendNode(nested.Node);
                        break;
                    case MirTextRun run:
                        AddPart("Run", $"text: {Quote(run.Value)}");
                        break;
                    case MirTextSlot slot:
                        AddPart("Run", $"text: {slots[slot.SlotId]}");
                        break;
                }
            }
            AddPart("Close", $"node: {carrier}");
        }

        void AddPart(string tag, string fields)
            => parts.Add($"Object.freeze({{ tag: {Quote(tag)}, payload: Object.freeze({{ {fields} }}) }})");

        static string Attribute(MirTextNode node, string name)
            => node.Attributes.FirstOrDefault(attribute => attribute.Name == name)?.Value ?? "";

        static string Quote(string value) => JavaScriptLiteralWriter.WriteString(value);
    }
}
