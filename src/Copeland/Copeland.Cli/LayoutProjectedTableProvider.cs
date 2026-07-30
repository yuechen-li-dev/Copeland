using Copeland.TS.Compiler;
using Copeland.Markdown;
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
    public const string TextRegions = "text::Regions";
    public const string TextDocuments = "text::Documents";
    public const string TextBlocks = "text::Blocks";
    public const string TextInlines = "text::Inlines";
    public const string TextBindings = "text::Bindings";
    public const string ComponentDefinitions = "component::Definitions";
    public const string ComponentInstances = "component::Instances";
    public const string ComponentBindings = "component::Bindings";
    public const string ComponentCaptures = "component::Captures";
    public const string ComponentLocalPresentations = "component::LocalPresentations";
    public const string RendererAdapters = "renderer::Adapters";
    public const string RendererAttachments = "renderer::Attachments";
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
        var textRegionRows = new List<IReadOnlyDictionary<string, object?>>();
        var textDocumentRows = new List<IReadOnlyDictionary<string, object?>>();
        var textBlockRows = new List<IReadOnlyDictionary<string, object?>>();
        var textInlineRows = new List<IReadOnlyDictionary<string, object?>>();
        var textBindingRows = new List<IReadOnlyDictionary<string, object?>>();
        var componentDefinitionRows = new List<IReadOnlyDictionary<string, object?>>();
        var componentInstanceRows = new List<IReadOnlyDictionary<string, object?>>();
        var componentBindingRows = new List<IReadOnlyDictionary<string, object?>>();
        var componentCaptureRows = new List<IReadOnlyDictionary<string, object?>>();
        var componentLocalPresentationRows = new List<IReadOnlyDictionary<string, object?>>();
        var rendererAdapterRows = new List<IReadOnlyDictionary<string, object?>>();
        var rendererAttachmentRows = new List<IReadOnlyDictionary<string, object?>>();
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
                    ("paintKey", box.PaintKey), ("overflowPolicy", box.OverflowPolicy), ("overflowX", box.OverflowX), ("overflowY", box.OverflowY), ("sourceId", boxSourceId)));
                if (box.Content is not null)
                {
                    string bindingId = box.SemanticPath + "::binding";
                    bindingRows.Add(Row(("bindingId", bindingId), ("boxId", box.SemanticPath), ("kind", box.Content.Kind), ("symbol", box.Content.Symbol), ("display", box.Content.Display), ("sourceId", boxSourceId)));
                }
                if (box.TextPolicy is not null)
                {
                    string textRegionId = box.SemanticPath + "::text";
                    string? textSourceId = AddSource(box.TextPolicy.Source);
                    textRegionRows.Add(Row(
                        ("textRegionId", textRegionId), ("boxId", box.SemanticPath), ("preferredFontSize", box.TextPolicy.PreferredFontSize),
                        ("minimumFontSize", box.TextPolicy.MinimumFontSize), ("maximumLines", box.TextPolicy.MaximumLines),
                        ("wrapMode", box.TextPolicy.WrapMode), ("fitMode", box.TextPolicy.FitMode), ("fallbackMode", box.TextPolicy.FallbackMode),
                        ("sourceId", textSourceId)));
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

        foreach (BoundComponentDefinition definition in compilation.Modules
            .SelectMany(module => module.BoundCompilation?.Program.ComponentDefinitions ?? [])
            .OrderBy(definition => definition.StableIdentity, StringComparer.Ordinal))
        {
            componentDefinitionRows.Add(Row(
                ("definitionId", definition.StableIdentity),
                ("name", definition.Function.Name),
                ("props", definition.Function.Parameters.Select(parameter => parameter.Name + ": " + parameter.Type.Name).ToArray()),
                ("implementationKind", definition.ImplementationKind.ToString()),
                ("presentationKind", definition.Presentation.Kind.ToString()),
                ("localLayoutId", definition.LocalStream is null ? null : definition.LocalStream.Layout.StableIdentity),
                ("rendererAdapter", definition.RendererAdapter.ToString()),
                ("requiredContentCapabilities", definition.RequiredContentCapabilities.ToString()),
                ("requiredHostCapabilities", definition.RequiredHostCapabilities.ToString()),
                ("payloadContract", definition.Presentation.PayloadContract)));

            if (definition.LocalStream is { IsPrivate: true } localPresentation)
            {
                componentLocalPresentationRows.Add(Row(
                    ("localPresentationId", localPresentation.Layout.StableIdentity),
                    ("definitionId", definition.StableIdentity),
                    ("name", localPresentation.Layout.Name),
                    ("rootBox", localPresentation.Realization.Root.Name),
                    ("accessibility", localPresentation.IsPrivate ? "private" : "public"),
                    ("implementationKind", definition.ImplementationKind.ToString())));
            }

            foreach (BoundComponentCapture capture in definition.Captures)
            {
                componentCaptureRows.Add(Row(
                    ("captureId", capture.StableIdentity),
                    ("definitionId", definition.StableIdentity),
                    ("name", capture.Source.Name),
                    ("kind", capture.Kind.ToString()),
                    ("type", capture.Type.Name),
                    ("sourceSymbol", capture.Source.Name)));
            }
        }

        foreach (BoundComponentInstance instance in compilation.Modules
            .SelectMany(module => module.BoundCompilation?.Program.ComponentInstances ?? [])
            .OrderBy(instance => instance.StableIdentity, StringComparer.Ordinal))
        {
            componentInstanceRows.Add(Row(
                ("instanceId", instance.StableIdentity),
                ("definitionId", instance.Definition.StableIdentity),
                ("parentComponentInstanceId", instance.ParentComponentInstance?.StableIdentity),
                ("parentHostBoxId", instance.ParentHostBox),
                ("authoredCallIdentity", instance.AuthoredCallIdentity),
                ("mountIdentity", instance.StableIdentity + "::mount"),
                ("suppliedHostCapabilities", instance.HostCapabilities.ToString()),
                ("localRoot", instance.Definition.LocalStream is null ? null : instance.Definition.LocalStream.Layout.Name + "." + instance.Definition.LocalStream.Realization.Root.Name),
                ("rendererAdapter", instance.Definition.RendererAdapter.ToString()),
                ("ordinal", instance.Ordinal)));

            for (int index = 0; index < instance.Props.Count; index += 1)
            {
                string parameter = index < instance.Definition.Function.Parameters.Count
                    ? instance.Definition.Function.Parameters[index].Name
                    : "arg" + index;
                componentBindingRows.Add(Row(
                    ("bindingId", instance.StableIdentity + "::props::" + parameter),
                    ("instanceId", instance.StableIdentity),
                    ("parameter", parameter),
                    ("valueType", instance.Props[index].Type.Name),
                    ("valueKind", instance.Props[index].GetType().Name)));
            }
        }

        foreach (HostAttachmentMir attachment in compilation.Modules
            .SelectMany(module => module.BoundCompilation?.Program.HostAttachments ?? [])
            .OrderBy(attachment => attachment.AttachmentId, StringComparer.Ordinal))
        {
            CopelandProjectModuleCompilation module = compilation.Modules.Single(module => module.BoundCompilation?.Program.HostAttachments.Contains(attachment) == true);
            string relativePath = Path.GetRelativePath(projectRoot, module.Source.SourcePath).Replace('\\', '/');
            int sourceStart = module.BoundCompilation!.Program.ComponentInstances
                .Single(instance => instance.StableIdentity == attachment.ComponentInstanceId)
                .ParentBinding.Syntax.LayoutIdentifier.Position;
            string sourceId = AddSource(new LayoutInspectionSource(relativePath, sourceStart, sourceStart + 1))!;
            rendererAttachmentRows.Add(Row(
                ("attachmentId", attachment.AttachmentId),
                ("componentDefinitionId", attachment.ComponentDefinitionId),
                ("componentInstanceId", attachment.ComponentInstanceId),
                ("parentComponentInstanceId", attachment.ParentComponentInstanceId),
                ("hostBoxId", attachment.HostBoxId),
                ("adapterId", attachment.AdapterId.ToString()),
                ("requiredHostCapabilities", attachment.RequiredHostCapabilities.ToString()),
                ("suppliedHostCapabilities", attachment.SuppliedHostCapabilities.ToString()),
                ("requiredContentCapabilities", attachment.RequiredContentCapabilities.ToString()),
                ("payloadContract", attachment.PayloadContract),
                ("lifecyclePolicy", attachment.LifecyclePolicy.ToString()),
                ("sourceId", sourceId)));
        }

        foreach (RendererAdapterContract adapter in RendererAdapterContracts.All)
        {
            rendererAdapterRows.Add(Row(
                ("adapterId", adapter.Identity.ToString()),
                ("supportedContentCapabilities", adapter.SupportedContentCapabilities.ToString()),
                ("requiredHostCapabilities", adapter.RequiredHostCapabilities.ToString()),
                ("browserAdapter", adapter.IsBrowserAdapter),
                ("payloadContracts", adapter.PayloadContracts.ToArray())));
        }

        IReadOnlyDictionary<string, LayoutInspectionBox> boxesById = documents
            .SelectMany(document => document.Boxes)
            .ToDictionary(box => box.SemanticPath, StringComparer.Ordinal);
        foreach (BoundLayoutBinding binding in compilation.Modules.SelectMany(module => module.BoundCompilation?.Program.LayoutBindings ?? []))
        {
            foreach (BoundLayoutBindingEntry entry in binding.Entries)
            {
                AddDocuments(entry.Slot.SemanticPath, entry.Slot.SemanticPath, entry.Component, entry.Slot.SemanticPath + "::binding");
            }
            foreach (BoundStreamCollection collection in binding.Collections)
            {
                string boxId = LayoutInspectionCommand.FindPath(binding.Layout.BoundLayout!.Root, collection.Region, binding.Layout.Name);
                for (int index = 0; index < collection.Items.Count; index += 1)
                {
                    AddDocuments(boxId + "::item::" + index, boxId, collection.Items[index], boxId + "::binding");
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
                new ProjectedTable(TextRegions, TextRegionSchema, textRegionRows.OrderBy(row => (string)row["textRegionId"]!, StringComparer.Ordinal).ToArray()),
                new ProjectedTable(TextDocuments, TextDocumentSchema, textDocumentRows.OrderBy(row => (string)row["documentId"]!, StringComparer.Ordinal).ToArray()),
                new ProjectedTable(TextBlocks, TextBlockSchema, textBlockRows.OrderBy(row => (string)row["documentId"]!, StringComparer.Ordinal).ThenBy(row => (int)row["authoredOrder"]!).ToArray()),
                new ProjectedTable(TextInlines, TextInlineSchema, textInlineRows.OrderBy(row => (string)row["blockId"]!, StringComparer.Ordinal).ThenBy(row => (int)row["authoredOrder"]!).ToArray()),
                new ProjectedTable(TextBindings, TextBindingSchema, textBindingRows.OrderBy(row => (string)row["bindingId"]!, StringComparer.Ordinal).ToArray()),
                new ProjectedTable(ComponentDefinitions, ComponentDefinitionSchema, componentDefinitionRows.OrderBy(row => (string)row["definitionId"]!, StringComparer.Ordinal).ToArray()),
                new ProjectedTable(ComponentInstances, ComponentInstanceSchema, componentInstanceRows.OrderBy(row => (string)row["instanceId"]!, StringComparer.Ordinal).ToArray()),
                new ProjectedTable(ComponentBindings, ComponentBindingSchema, componentBindingRows.OrderBy(row => (string)row["bindingId"]!, StringComparer.Ordinal).ToArray()),
                new ProjectedTable(ComponentCaptures, ComponentCaptureSchema, componentCaptureRows.OrderBy(row => (string)row["captureId"]!, StringComparer.Ordinal).ToArray()),
                new ProjectedTable(ComponentLocalPresentations, ComponentLocalPresentationSchema, componentLocalPresentationRows.OrderBy(row => (string)row["localPresentationId"]!, StringComparer.Ordinal).ToArray()),
                new ProjectedTable(RendererAdapters, RendererAdapterSchema, rendererAdapterRows.OrderBy(row => (string)row["adapterId"]!, StringComparer.Ordinal).ToArray()),
                new ProjectedTable(RendererAttachments, RendererAttachmentSchema, rendererAttachmentRows.OrderBy(row => (string)row["attachmentId"]!, StringComparer.Ordinal).ToArray()),
                new ProjectedTable(Sources, SourceSchema, sources.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (IReadOnlyDictionary<string, object?>)pair.Value).ToArray()),
            ]);

        void AddDocuments(string documentScopeId, string owningBoxId, BoundExpression component, string bindingId)
        {
            string? owner = LayoutInspectionCommand.Symbol(component);
            if (owner is null) return;
            LayoutInspectionBox? box = boxesById.GetValueOrDefault(owningBoxId);
            foreach (BoundTextDocument definition in compilation.TextDocuments.Where(document => document.OwnerFunction == owner))
            {
                string semanticRole = definition.Document.Blocks.FirstOrDefault()?.Metadata.Role
                    ?? definition.Document.Blocks.FirstOrDefault()?.GetType().Name
                    ?? "Document";
                string documentId = documentScopeId + "::text::" + owner + "::" + semanticRole;
                string? sourceId = AddSource(DocumentSource(definition.Document.Metadata.Provenance));
                textDocumentRows.Add(Row(
                    ("documentId", documentId), ("owningBoxId", owningBoxId), ("bindingId", bindingId), ("themeId", "CopelandText"),
                    ("fitMode", box?.TextPolicy?.FitMode ?? "none"), ("overflowPolicy", box?.OverflowPolicy ?? "visible"), ("sourceId", sourceId)));
                textBindingRows.Add(Row(
                    ("bindingId", documentId + "::presentation"), ("documentId", documentId), ("owningBoxId", owningBoxId),
                    ("semanticHostId", definition.Presentation.SemanticHostId), ("themeId", definition.Presentation.ThemeId),
                    ("fitMode", box?.TextPolicy?.FitMode ?? "none"), ("preferredFontSize", box?.TextPolicy?.PreferredFontSize),
                    ("minimumFontSize", box?.TextPolicy?.MinimumFontSize), ("lineLimit", box?.TextPolicy?.MaximumLines),
                    ("wrapMode", box?.TextPolicy?.WrapMode), ("overflowPolicy", box?.OverflowPolicy ?? "visible"),
                    ("documentClassName", definition.Presentation.DocumentClassName), ("sourceId", AddSource(DocumentSource(definition.Presentation.Source)))));

                foreach (DocumentBlockMir block in definition.Document.Blocks)
                {
                    AddBlock(block);
                }

                void AddBlock(DocumentBlockMir block)
                {
                    string blockId = ProjectedNodeId(block.Metadata.NodeId);
                    textBlockRows.Add(Row(
                        ("blockId", blockId), ("documentId", documentId), ("parentBlockId", ProjectedNodeId(block.Metadata.ParentNodeId)),
                        ("kind", BlockKind(block)), ("role", block.Metadata.Role), ("authoredOrder", block.Metadata.AuthoredOrder), ("sourceId", AddSource(DocumentSource(block.Metadata.Provenance)))));
                    switch (block)
                    {
                        case HeadingMir heading:
                            AddInlines(heading.Inlines, blockId, heading.Metadata.Role);
                            break;
                        case ParagraphMir paragraph:
                            AddInlines(paragraph.Inlines, blockId, paragraph.Metadata.Role);
                            break;
                        case QuoteMir quote:
                            AddInlines(quote.Inlines, blockId, quote.Metadata.Role);
                            break;
                        case CalloutMir callout:
                            AddInlines(callout.Inlines, blockId, callout.Metadata.Role);
                            break;
                        case CodeBlockMir code:
                            AddCodeBlockInline(code, blockId);
                            break;
                        case ListMir list:
                            foreach (ListItemMir item in list.Items) AddListItem(item);
                            break;
                    }
                }

                void AddListItem(ListItemMir item)
                {
                    string blockId = ProjectedNodeId(item.Metadata.NodeId);
                    textBlockRows.Add(Row(
                        ("blockId", blockId), ("documentId", documentId), ("parentBlockId", ProjectedNodeId(item.Metadata.ParentNodeId)),
                        ("kind", "ListItem"), ("role", item.Metadata.Role), ("authoredOrder", item.Metadata.AuthoredOrder), ("sourceId", AddSource(DocumentSource(item.Metadata.Provenance)))));
                    AddInlines(item.Inlines, blockId, item.Metadata.Role);
                    foreach (DocumentBlockMir child in item.ChildBlocks) AddBlock(child);
                }

                void AddCodeBlockInline(CodeBlockMir code, string blockId)
                {
                    string inlineId = blockId + "::literal";
                    textInlineRows.Add(Row(
                        ("inlineId", inlineId), ("blockId", blockId), ("parentInlineId", null), ("kind", "TextRun"), ("authoredOrder", 0),
                        ("text", code.Text), ("target", null), ("role", "CodeBlock"), ("sourceId", AddSource(DocumentSource(code.Metadata.Provenance)))));
                }

                void AddInlines(IReadOnlyList<DocumentInlineMir> inlines, string blockId, string? role)
                {
                    foreach (DocumentInlineMir inline in inlines)
                    {
                        AddInline(inline, blockId, role);
                    }
                }

                void AddInline(DocumentInlineMir inline, string blockId, string? role)
                {
                    textInlineRows.Add(Row(
                        ("inlineId", ProjectedNodeId(inline.Metadata.NodeId)), ("blockId", blockId), ("parentInlineId", ProjectedNodeId(inline.Metadata.ParentNodeId)),
                        ("kind", InlineKind(inline)), ("authoredOrder", inline.Metadata.AuthoredOrder), ("text", InlineText(inline)), ("target", InlineTarget(inline)),
                        ("role", role), ("sourceId", AddSource(DocumentSource(inline.Metadata.Provenance)))));
                    switch (inline)
                    {
                        case EmphasisMir emphasis:
                            foreach (DocumentInlineMir child in emphasis.Children) AddInline(child, blockId, role);
                            break;
                        case StrongMir strong:
                            foreach (DocumentInlineMir child in strong.Children) AddInline(child, blockId, role);
                            break;
                        case LinkMir link:
                            foreach (DocumentInlineMir child in link.Label) AddInline(child, blockId, role);
                            break;
                    }
                }

                string ProjectedNodeId(string? canonicalId)
                    => canonicalId is null ? null! : documentId + "::" + canonicalId;
            }
        }

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

        LayoutInspectionSource DocumentSource(DocumentProvenance source)
            => new(Path.GetRelativePath(projectRoot, source.SourcePath).Replace('\\', '/'), source.Start, source.End);
    }

    private static string BlockKind(DocumentBlockMir block) => block switch
    {
        HeadingMir => "Heading",
        ParagraphMir => "Paragraph",
        ListMir => "List",
        CodeBlockMir => "CodeBlock",
        QuoteMir => "Quote",
        CalloutMir => "Callout",
        BreakMir => "Break",
        ThematicBreakMir => "ThematicBreak",
        _ => block.GetType().Name,
    };

    private static string InlineKind(DocumentInlineMir inline) => inline switch
    {
        TextMir => "TextRun",
        CodeSpanMir => "InlineCode",
        StrongMir => "Strong",
        EmphasisMir => "Emphasis",
        LinkMir => "Link",
        _ => inline.GetType().Name,
    };

    private static string InlineText(DocumentInlineMir inline) => inline switch
    {
        TextMir text => text.Text,
        CodeSpanMir code => code.Text,
        _ => string.Empty,
    };

    private static string? InlineTarget(DocumentInlineMir inline)
        => inline is LinkMir link ? link.Target : null;

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
        new("authoredOrder", "int"), new("paintOrder", "int"), new("paintKey", "paintOrderKey"), new("overflowPolicy", "overflowPolicy"), new("overflowX", "overflowAxis"), new("overflowY", "overflowAxis"), new("sourceId", "foreignKey<Sources>?"),
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
    private static readonly IReadOnlyList<ProjectedColumn> TextRegionSchema =
    [ new("textRegionId", "identity"), new("boxId", "foreignKey<Boxes>"), new("preferredFontSize", "px"), new("minimumFontSize", "px"), new("maximumLines", "int"), new("wrapMode", "textWrapMode"), new("fitMode", "textFitMode"), new("fallbackMode", "textFallbackMode"), new("sourceId", "foreignKey<Sources>?") ];
    private static readonly IReadOnlyList<ProjectedColumn> TextDocumentSchema =
    [ new("documentId", "identity"), new("owningBoxId", "foreignKey<Boxes>"), new("bindingId", "foreignKey<Bindings>"), new("themeId", "identity"), new("fitMode", "textFitMode"), new("overflowPolicy", "overflowPolicy"), new("sourceId", "foreignKey<Sources>") ];
    private static readonly IReadOnlyList<ProjectedColumn> TextBlockSchema =
    [ new("blockId", "identity"), new("documentId", "foreignKey<Documents>"), new("parentBlockId", "foreignKey<Blocks>?"), new("kind", "textBlockKind"), new("role", "textRole?"), new("authoredOrder", "int"), new("sourceId", "foreignKey<Sources>") ];
    private static readonly IReadOnlyList<ProjectedColumn> TextInlineSchema =
    [ new("inlineId", "identity"), new("blockId", "foreignKey<Blocks>"), new("parentInlineId", "foreignKey<Inlines>?"), new("kind", "textInlineKind"), new("authoredOrder", "int"), new("text", "string"), new("target", "url?"), new("role", "textRole?"), new("sourceId", "foreignKey<Sources>") ];
    private static readonly IReadOnlyList<ProjectedColumn> TextBindingSchema =
    [ new("bindingId", "identity"), new("documentId", "foreignKey<Documents>"), new("owningBoxId", "foreignKey<Boxes>"), new("semanticHostId", "identity"), new("themeId", "identity"), new("fitMode", "textFitMode"), new("preferredFontSize", "px?"), new("minimumFontSize", "px?"), new("lineLimit", "int?"), new("wrapMode", "textWrapMode?"), new("overflowPolicy", "overflowPolicy"), new("documentClassName", "presentationClass?"), new("sourceId", "foreignKey<Sources>") ];
    private static readonly IReadOnlyList<ProjectedColumn> ComponentDefinitionSchema =
    [ new("definitionId", "identity"), new("name", "string"), new("props", "componentProps"), new("implementationKind", "componentImplementationKind"), new("presentationKind", "componentPresentationKind"), new("localLayoutId", "identity?"), new("rendererAdapter", "rendererAdapter"), new("requiredContentCapabilities", "contentCapabilitySet"), new("requiredHostCapabilities", "hostCapabilitySet"), new("payloadContract", "rendererPayloadContract") ];
    private static readonly IReadOnlyList<ProjectedColumn> ComponentInstanceSchema =
    [ new("instanceId", "identity"), new("definitionId", "foreignKey<Definitions>"), new("parentComponentInstanceId", "foreignKey<Instances>?"), new("parentHostBoxId", "identity"), new("authoredCallIdentity", "identity"), new("mountIdentity", "identity"), new("suppliedHostCapabilities", "hostCapabilitySet"), new("localRoot", "identity?"), new("rendererAdapter", "rendererAdapter"), new("ordinal", "int") ];
    private static readonly IReadOnlyList<ProjectedColumn> ComponentBindingSchema =
    [ new("bindingId", "identity"), new("instanceId", "foreignKey<Instances>"), new("parameter", "string"), new("valueType", "type"), new("valueKind", "componentArgumentKind") ];
    private static readonly IReadOnlyList<ProjectedColumn> ComponentCaptureSchema =
    [ new("captureId", "identity"), new("definitionId", "foreignKey<Definitions>"), new("name", "string"), new("kind", "componentCaptureKind"), new("type", "type"), new("sourceSymbol", "identity") ];
    private static readonly IReadOnlyList<ProjectedColumn> ComponentLocalPresentationSchema =
    [ new("localPresentationId", "identity"), new("definitionId", "foreignKey<Definitions>"), new("name", "string"), new("rootBox", "identity"), new("accessibility", "accessibility"), new("implementationKind", "componentImplementationKind") ];
    private static readonly IReadOnlyList<ProjectedColumn> RendererAdapterSchema =
    [ new("adapterId", "identity"), new("supportedContentCapabilities", "contentCapabilitySet"), new("requiredHostCapabilities", "hostCapabilitySet"), new("browserAdapter", "bool"), new("payloadContracts", "rendererPayloadContractSet") ];
    private static readonly IReadOnlyList<ProjectedColumn> RendererAttachmentSchema =
    [ new("attachmentId", "identity"), new("componentDefinitionId", "foreignKey<Definitions>"), new("componentInstanceId", "foreignKey<Instances>"), new("parentComponentInstanceId", "foreignKey<Instances>?"), new("hostBoxId", "foreignKey<Boxes>"), new("adapterId", "foreignKey<Adapters>"), new("requiredHostCapabilities", "hostCapabilitySet"), new("suppliedHostCapabilities", "hostCapabilitySet"), new("requiredContentCapabilities", "contentCapabilitySet"), new("payloadContract", "rendererPayloadContract"), new("lifecyclePolicy", "attachmentLifecyclePolicy"), new("sourceId", "foreignKey<Sources>") ];
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
