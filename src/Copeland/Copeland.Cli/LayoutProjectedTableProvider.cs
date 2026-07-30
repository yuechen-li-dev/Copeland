using Copeland.TS.Compiler;
using Copeland.TS.MachinaSource;
using Copeland.TS.Semantics.Bound;

namespace Copeland.Cli;

/// <summary>
/// Read-only relational tables projected from normalized compiler layout data.
/// The rows are never copied into authored source tables.
/// </summary>
internal static class LayoutProjectedTableProvider
{
    public const string Layouts = "layout::Layouts";
    public const string Boxes = "layout::Boxes";
    public const string Derivations = "layout::Derivations";
    public const string Bindings = "layout::Bindings";
    public const string CollectionItems = "layout::CollectionItems";
    public const string Sources = "layout::Sources";

    public static ProjectedTableSet Create(CopelandProjectCompilation compilation, string projectRoot)
    {
        var documents = new List<LayoutInspectionDocument>();
        foreach (CopelandProjectModuleCompilation module in compilation.Modules.OrderBy(module => module.LogicalPath, StringComparer.Ordinal))
        {
            if (module.BoundCompilation is null) continue;
            foreach (BoundLayoutDeclaration layout in module.BoundCompilation.Program.Layouts.OrderBy(layout => layout.Name, StringComparer.Ordinal))
            {
                LayoutInspectionDocument inspection = LayoutInspection.Create(layout, module.Source.SourcePath, projectRoot);
                documents.Add(inspection with { Boxes = LayoutInspectionCommand.AttachContent(inspection.Boxes, compilation, layout) });
            }
        }

        var sources = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        IReadOnlyDictionary<string, string> sourceTextByPath = compilation.Modules
            .ToDictionary(module => Path.GetRelativePath(projectRoot, module.Source.SourcePath).Replace('\\', '/'), module => module.Source.SourceText, StringComparer.Ordinal);
        var layoutRows = new List<IReadOnlyDictionary<string, object?>>();
        var boxRows = new List<IReadOnlyDictionary<string, object?>>();
        var derivationRows = new List<IReadOnlyDictionary<string, object?>>();
        var bindingRows = new List<IReadOnlyDictionary<string, object?>>();
        var collectionItemRows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (LayoutInspectionDocument document in documents)
        {
            string layoutId = document.Layout.Module + "::" + document.Layout.Name;
            string? sourceId = AddSource(document.Boxes.FirstOrDefault(box => box.Source is not null)?.Source);
            layoutRows.Add(Row(
                ("layoutId", layoutId), ("name", document.Layout.Name), ("module", document.Layout.Module), ("profile", document.Layout.Profile),
                ("originX", document.Layout.OriginX), ("originY", document.Layout.OriginY), ("width", document.Layout.Width), ("height", document.Layout.Height),
                ("layerSet", document.Layout.LayerSet), ("contract", document.Layout.Contract), ("conformance", document.Layout.Conformance), ("sourceId", sourceId)));
            foreach (LayoutInspectionBox box in document.Boxes)
            {
                string? boxSourceId = AddSource(box.Source);
                boxRows.Add(Row(
                    ("boxId", box.SemanticPath), ("layoutId", layoutId), ("semanticPath", box.SemanticPath), ("parentBoxId", box.Parent), ("kind", box.Kind),
                    ("x", box.OriginX), ("y", box.OriginY), ("width", box.Width), ("height", box.Height), ("layer", box.Layer),
                    ("layerRank", box.LayerRank), ("z", box.Z), ("authoredOrder", box.AuthoredOrder), ("paintOrder", box.PaintOrder),
                    ("paintKey", box.PaintKey), ("sourceId", boxSourceId)));
                if (box.Content is not null)
                {
                    string bindingId = box.SemanticPath + "::binding";
                    bindingRows.Add(Row(("bindingId", bindingId), ("boxId", box.SemanticPath), ("kind", box.Content.Kind), ("symbol", box.Content.Symbol), ("display", box.Content.Display), ("sourceId", boxSourceId)));
                }
            }
            foreach (LayoutInspectionDerivation derivation in document.Derivations ?? [])
            {
                string? derivationSourceId = AddSource(derivation.Source);
                derivationRows.Add(Row(
                    ("derivationId", derivation.DerivationId), ("layoutId", layoutId), ("targetBoxId", derivation.TargetBoxId),
                    ("transform", derivation.Transform), ("sourceBoxId", derivation.SourceBoxId),
                    ("fieldsRead", derivation.FieldsRead), ("fieldsWritten", derivation.FieldsWritten),
                    ("authoredOrder", derivation.AuthoredOrder), ("status", derivation.Status), ("gapOrPadding", derivation.GapOrPadding), ("sourceId", derivationSourceId)));
            }
        }

        foreach (BoundLayoutBinding binding in compilation.Modules.SelectMany(module => module.BoundCompilation?.Program.LayoutBindings ?? []))
        {
            foreach (BoundStreamCollection collection in binding.Collections)
            {
                string boxId = LayoutInspectionCommand.FindPath(binding.Layout.BoundLayout!.Root, collection.Region, binding.Layout.Name);
                string bindingId = boxId + "::binding";
                for (int index = 0; index < collection.Items.Count; index += 1)
                {
                    BoundExpression item = collection.Items[index];
                    collectionItemRows.Add(Row(("bindingId", bindingId), ("itemIndex", index), ("symbol", LayoutInspectionCommand.Symbol(item)), ("display", LayoutInspectionCommand.Display(item)), ("sourceId", null)));
                }
            }
        }

        return new ProjectedTableSet(
            documents,
            [
                new ProjectedTable(Layouts, LayoutSchema, layoutRows),
                new ProjectedTable(Boxes, BoxSchema, boxRows),
                new ProjectedTable(Derivations, DerivationSchema, derivationRows.OrderBy(row => (string)row["layoutId"]!, StringComparer.Ordinal).ThenBy(row => (int)row["authoredOrder"]!).ToArray()),
                new ProjectedTable(Bindings, BindingSchema, bindingRows.OrderBy(row => (string)row["bindingId"]!, StringComparer.Ordinal).ToArray()),
                new ProjectedTable(CollectionItems, CollectionItemSchema, collectionItemRows.OrderBy(row => (string)row["bindingId"]!, StringComparer.Ordinal).ThenBy(row => (int)row["itemIndex"]!).ToArray()),
                new ProjectedTable(Sources, SourceSchema, sources.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (IReadOnlyDictionary<string, object?>)pair.Value).ToArray()),
            ]);

        string? AddSource(LayoutInspectionSource? source)
        {
            if (source is null) return null;
            string id = source.Path + ":" + source.Start + "+" + (source.End - source.Start);
            if (!sources.ContainsKey(id))
            {
                string sourceText = sourceTextByPath.GetValueOrDefault(source.Path, string.Empty);
                (int startLine, int startColumn) = LineColumn(sourceText, source.Start);
                (int endLine, int endColumn) = LineColumn(sourceText, source.End);
                sources.Add(id, Row(("sourceId", id), ("projectRelativePath", source.Path), ("startLine", startLine), ("startColumn", startColumn), ("endLine", endLine), ("endColumn", endColumn)));
            }
            return id;
        }
    }

    private static (int Line, int Column) LineColumn(string source, int position)
    {
        int line = 1;
        int column = 1;
        for (int index = 0; index < Math.Min(position, source.Length); index += 1)
        {
            if (source[index] == '\n') { line += 1; column = 1; }
            else { column += 1; }
        }
        return (line, column);
    }

    private static Dictionary<string, object?> Row(params (string Name, object? Value)[] values)
        => values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    private static readonly IReadOnlyList<ProjectedColumn> LayoutSchema =
    [
        new("layoutId", "identity"), new("name", "string"), new("module", "path"), new("profile", "string?"), new("originX", "constraint"), new("originY", "constraint"),
        new("width", "constraint"), new("height", "constraint"), new("layerSet", "identity"), new("contract", "identity?"), new("conformance", "bool?"), new("sourceId", "foreignKey<Sources>?"),
    ];
    private static readonly IReadOnlyList<ProjectedColumn> BoxSchema =
    [
        new("boxId", "identity"), new("layoutId", "foreignKey<Layouts>"), new("semanticPath", "identity"), new("parentBoxId", "foreignKey<Boxes>?"), new("kind", "layoutNodeKind"),
        new("x", "constraint"), new("y", "constraint"), new("width", "constraint"), new("height", "constraint"), new("layer", "identity"), new("layerRank", "int"), new("z", "int"),
        new("authoredOrder", "int"), new("paintOrder", "int"), new("paintKey", "paintOrderKey"), new("sourceId", "foreignKey<Sources>?"),
    ];
    private static readonly IReadOnlyList<ProjectedColumn> DerivationSchema =
    [
        new("derivationId", "identity"), new("layoutId", "foreignKey<Layouts>"), new("targetBoxId", "foreignKey<Boxes>"), new("transform", "layoutRelativeTransform"),
        new("sourceBoxId", "foreignKey<Boxes>"), new("fieldsRead", "fieldSet"), new("fieldsWritten", "fieldSet"), new("authoredOrder", "int"),
        new("status", "derivationStatus"), new("gapOrPadding", "constraint?"), new("sourceId", "foreignKey<Sources>"),
    ];
    private static readonly IReadOnlyList<ProjectedColumn> BindingSchema =
    [ new("bindingId", "identity"), new("boxId", "foreignKey<Boxes>"), new("kind", "bindingKind"), new("symbol", "identity?"), new("display", "string"), new("sourceId", "foreignKey<Sources>?") ];
    private static readonly IReadOnlyList<ProjectedColumn> CollectionItemSchema =
    [ new("bindingId", "foreignKey<Bindings>"), new("itemIndex", "int"), new("symbol", "identity?"), new("display", "string"), new("sourceId", "foreignKey<Sources>?") ];
    private static readonly IReadOnlyList<ProjectedColumn> SourceSchema =
    [ new("sourceId", "identity"), new("projectRelativePath", "path"), new("startLine", "int"), new("startColumn", "int"), new("endLine", "int"), new("endColumn", "int") ];
}

internal sealed record ProjectedTableSet(IReadOnlyList<LayoutInspectionDocument> LayoutViews, IReadOnlyList<ProjectedTable> Tables)
{
    public ProjectedTable Require(string name) => Tables.SingleOrDefault(table => table.Name == name)
        ?? throw new InvalidOperationException($"Projected table '{name}' was not found.");
}

internal sealed record ProjectedTable(string Name, IReadOnlyList<ProjectedColumn> Columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);
internal sealed record ProjectedColumn(string Name, string Type);
