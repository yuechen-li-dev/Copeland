using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Copeland.SpanAllocation;
using Copeland.TS.Compiler;
using Copeland.TS.Diagnostics;
using Copeland.TS.Semantics.Bound;

namespace Copeland.TS.Assets;

/// <summary>
/// Explicit compiler profile for programmable object assets. The .obj.ts
/// suffix alone never selects this profile, so existing static TSON documents
/// keep their established meaning.
/// </summary>
public static class ObjectAssetCompiler
{
    public const string RootBindingName = "$asset";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static ObjectAssetCompilationResult CompileFile(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        string fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
        {
            return Failure(new Diagnostic(
                "COPE-ASSET-0001",
                $"Object asset source '{fullPath}' does not exist.",
                0,
                1,
                fullPath));
        }

        return Compile(File.ReadAllText(fullPath), fullPath);
    }

    public static ObjectAssetCompilationResult Compile(string source, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        string fullPath = Path.GetFullPath(sourcePath);
        if (!fullPath.EndsWith(".obj.ts", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(new Diagnostic(
                "COPE-ASSET-0002",
                "Programmable object asset sources must use the '.obj.ts' suffix.",
                0,
                Math.Max(1, source.Length),
                fullPath));
        }

        CopelandCompilation compilation = CopelandCompiler.Compile(
            source,
            new CopelandCompilationOptions
            {
                TargetStage = CopelandCompilationStage.Bound,
                SourcePath = fullPath,
                ProjectRoot = Path.GetDirectoryName(fullPath),
            });
        if (!compilation.Success || compilation.BoundCompilation is null)
        {
            return new ObjectAssetCompilationResult(null, compilation.Diagnostics);
        }

        BoundVariableDeclaration[] roots = compilation.BoundCompilation.Program.GlobalStatements
            .OfType<BoundVariableDeclaration>()
            .Where(variable => variable.Variable.Name == RootBindingName)
            .ToArray();
        if (roots.Length != 1)
        {
            return Failure(AtRoot(
                source,
                fullPath,
                "COPE-ASSET-0003",
                $"A programmable object asset requires exactly one 'const {RootBindingName}' binding; found {roots.Length}."));
        }

        BoundExpression value = roots[0].Initializer is BoundStaticExpression staticExpression
            ? staticExpression.EvaluatedExpression ?? staticExpression.Expression
            : roots[0].Initializer;
        if (roots[0].Initializer is not BoundStaticExpression)
        {
            return Failure(AtRoot(
                source,
                fullPath,
                "COPE-ASSET-0004",
                $"'{RootBindingName}' must be initialized with a 'static' expression so runtime tooling receives resolved metadata."));
        }

        var diagnostics = new List<Diagnostic>();
        ObjectAssetDocument? document = DecodeDocument(
            value,
            compilation.BoundCompilation,
            source,
            fullPath,
            diagnostics);
        if (document is not null)
        {
            Validate(document, source, fullPath, diagnostics);
        }

        return diagnostics.Count == 0
            ? new ObjectAssetCompilationResult(document, [])
            : new ObjectAssetCompilationResult(null, diagnostics);
    }

    public static ObjectAssetBuildOutputs Emit(ObjectAssetDocument document, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        string normalizedSource = Path.GetFileName(sourcePath).Replace('\\', '/');
        string json = JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
        string audit = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            source = normalizedSource,
            sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(sourcePath)))).ToLowerInvariant(),
            document.Id,
            regionCount = document.Regions.Count,
            panelCount = document.Panels.Count,
            minimumSizes = document.Panels.ToDictionary(
                panel => panel.Id,
                panel => new { width = panel.MinimumWidth, height = panel.MinimumHeight },
                StringComparer.Ordinal),
            diagnostics = Array.Empty<object>(),
        }, JsonOptions) + Environment.NewLine;
        return new ObjectAssetBuildOutputs(
            EmitToml(document, normalizedSource, "generated-obj-ts"),
            EmitToml(document, normalizedSource, "runtime-toml"),
            json,
            audit);
    }

    private static ObjectAssetDocument? DecodeDocument(
        BoundExpression expression,
        BoundCompilation compilation,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        if (!TryRecord(expression, "AssetObject", out Dictionary<string, BoundExpression> fields))
        {
            diagnostics.Add(AtRoot(source, sourcePath, "COPE-ASSET-0005", "'$asset' must resolve to an AssetObject record."));
            return null;
        }

        int schemaVersion = Int(fields, "schemaVersion", source, sourcePath, diagnostics);
        string id = String(fields, "id", source, sourcePath, diagnostics);
        ObjectAssetTexture? texture = DecodeTexture(Value(fields, "texture", source, sourcePath, diagnostics), source, sourcePath, diagnostics);
        IReadOnlyList<ObjectAssetRegion> regions = DecodeRegions(
            compilation,
            source,
            sourcePath,
            diagnostics);
        IReadOnlyList<ObjectAssetPanel> panels = DecodeArray(
            Value(fields, "panels", source, sourcePath, diagnostics),
            item => DecodePanel(item, source, sourcePath, diagnostics),
            source,
            sourcePath,
            diagnostics,
            "panels");
        IReadOnlyDictionary<string, ObjectAssetRegion> regionsById = regions
            .GroupBy(region => region.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        panels = panels.Select(panel => WithMinimumSize(panel, regionsById)).ToArray();
        return texture is null
            ? null
            : new ObjectAssetDocument(schemaVersion, id, texture, regions, panels);
    }

    private static ObjectAssetTexture? DecodeTexture(
        BoundExpression? expression,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        if (!TryRecord(expression, "AssetTexture", out Dictionary<string, BoundExpression> fields))
        {
            diagnostics.Add(AtRoot(source, sourcePath, "COPE-ASSET-0006", "texture must resolve to an AssetTexture record."));
            return null;
        }

        return new ObjectAssetTexture(
            String(fields, "id", source, sourcePath, diagnostics),
            String(fields, "source", source, sourcePath, diagnostics),
            Int(fields, "width", source, sourcePath, diagnostics),
            Int(fields, "height", source, sourcePath, diagnostics));
    }

    private static IReadOnlyList<ObjectAssetRegion> DecodeRegions(
        BoundCompilation compilation,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        BoundTableDefinition[] tables = compilation.Program.Tables
            .Where(table => table.TableType.Name == "AssetRegions")
            .ToArray();
        if (tables.Length != 1)
        {
            diagnostics.Add(AtRoot(source, sourcePath, "COPE-ASSET-0007", $"A programmable object asset requires exactly one columnar 'record table AssetRegions'; found {tables.Length}."));
            return [];
        }

        BoundTableDefinition table = tables[0];
        IReadOnlyDictionary<string, BoundTableColumnDefinition> columns = table.Columns
            .ToDictionary(column => column.Column.Name, StringComparer.Ordinal);
        string[] requiredColumns = ["id", "x", "y", "width", "height"];
        foreach (string columnName in requiredColumns)
        {
            if (!columns.ContainsKey(columnName))
            {
                diagnostics.Add(AtRoot(source, sourcePath, "COPE-ASSET-0007", $"AssetRegions is missing required column '{columnName}'."));
            }
        }

        if (diagnostics.Count > 0)
        {
            return [];
        }

        var result = new List<ObjectAssetRegion>(table.RowCount);
        for (int row = 0; row < table.RowCount; row++)
        {
            string id = TableString(columns["id"].Cells[row], "id", source, sourcePath, diagnostics);
            int x = TableInt(columns["x"].Cells[row], "x", source, sourcePath, diagnostics);
            int y = TableInt(columns["y"].Cells[row], "y", source, sourcePath, diagnostics);
            int width = TableInt(columns["width"].Cells[row], "width", source, sourcePath, diagnostics);
            int height = TableInt(columns["height"].Cells[row], "height", source, sourcePath, diagnostics);
            result.Add(new ObjectAssetRegion(id, x, y, width, height));
        }

        return result;
    }

    private static string TableString(
        BoundTableConstant value,
        string column,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        return value is BoundTableLiteralConstant { Value: string text }
            ? text
            : InvalidPrimitive<string>(column, "string table cell", source, sourcePath, diagnostics);
    }

    private static int TableInt(
        BoundTableConstant value,
        string column,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        return value is BoundTableLiteralConstant { Value: int number }
            ? number
            : InvalidPrimitive<int>(column, "int table cell", source, sourcePath, diagnostics);
    }

    private static ObjectAssetPanel? DecodePanel(
        BoundExpression expression,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        if (!TryRecord(expression, "AssetPanel", out Dictionary<string, BoundExpression> fields))
        {
            diagnostics.Add(AtRoot(source, sourcePath, "COPE-ASSET-0008", "panels must contain AssetPanel records."));
            return null;
        }

        ObjectAssetEdge top = DecodeEdge(Value(fields, "top", source, sourcePath, diagnostics), "top", source, sourcePath, diagnostics);
        ObjectAssetEdge right = DecodeEdge(Value(fields, "right", source, sourcePath, diagnostics), "right", source, sourcePath, diagnostics);
        ObjectAssetEdge bottom = DecodeEdge(Value(fields, "bottom", source, sourcePath, diagnostics), "bottom", source, sourcePath, diagnostics);
        ObjectAssetEdge left = DecodeEdge(Value(fields, "left", source, sourcePath, diagnostics), "left", source, sourcePath, diagnostics);
        string centerPolicyText = String(fields, "centerPolicy", source, sourcePath, diagnostics);
        ObjectAssetCenterPolicy centerPolicy = centerPolicyText switch
        {
            "analytic-fill" => ObjectAssetCenterPolicy.AnalyticFill,
            "stretch-region" => ObjectAssetCenterPolicy.StretchRegion,
            "tile-region" => ObjectAssetCenterPolicy.TileRegion,
            _ => InvalidCenterPolicy(centerPolicyText, source, sourcePath, diagnostics),
        };

        ObjectAssetPadding padding = DecodePadding(
            Value(fields, "contentPadding", source, sourcePath, diagnostics),
            source,
            sourcePath,
            diagnostics);
        double borderScale = Number(fields, "borderScale", source, sourcePath, diagnostics);
        int minimumWidth = Math.Max(top.MinimumLength, bottom.MinimumLength);
        int minimumHeight = Math.Max(left.MinimumLength, right.MinimumLength);

        return new ObjectAssetPanel(
            String(fields, "id", source, sourcePath, diagnostics),
            String(fields, "topLeftRegion", source, sourcePath, diagnostics),
            String(fields, "topRightRegion", source, sourcePath, diagnostics),
            String(fields, "bottomRightRegion", source, sourcePath, diagnostics),
            String(fields, "bottomLeftRegion", source, sourcePath, diagnostics),
            top,
            right,
            bottom,
            left,
            centerPolicy,
            String(fields, "centerRegion", source, sourcePath, diagnostics),
            borderScale,
            padding,
            minimumWidth,
            minimumHeight);
    }

    private static ObjectAssetPanel WithMinimumSize(
        ObjectAssetPanel panel,
        IReadOnlyDictionary<string, ObjectAssetRegion> regions)
    {
        int leftWidth = regions.GetValueOrDefault(panel.TopLeftRegionId)?.Width ?? 0;
        int rightWidth = regions.GetValueOrDefault(panel.TopRightRegionId)?.Width ?? 0;
        int topHeight = regions.GetValueOrDefault(panel.TopLeftRegionId)?.Height ?? 0;
        int bottomHeight = regions.GetValueOrDefault(panel.BottomLeftRegionId)?.Height ?? 0;
        int minimumWidth = checked((int)Math.Ceiling(
            Math.Max(panel.Top.MinimumLength, panel.Bottom.MinimumLength)
            + ((leftWidth + rightWidth) * panel.BorderScale)));
        int minimumHeight = checked((int)Math.Ceiling(
            Math.Max(panel.Left.MinimumLength, panel.Right.MinimumLength)
            + ((topHeight + bottomHeight) * panel.BorderScale)));
        return panel with { MinimumWidth = minimumWidth, MinimumHeight = minimumHeight };
    }

    private static ObjectAssetPadding DecodePadding(
        BoundExpression? expression,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        if (!TryRecord(expression, "AssetPadding", out Dictionary<string, BoundExpression> fields))
        {
            diagnostics.Add(AtRoot(source, sourcePath, "COPE-ASSET-0009", "contentPadding must resolve to an AssetPadding record."));
            return new ObjectAssetPadding(0, 0, 0, 0);
        }

        return new ObjectAssetPadding(
            Int(fields, "left", source, sourcePath, diagnostics),
            Int(fields, "top", source, sourcePath, diagnostics),
            Int(fields, "right", source, sourcePath, diagnostics),
            Int(fields, "bottom", source, sourcePath, diagnostics));
    }

    private static ObjectAssetEdge DecodeEdge(
        BoundExpression? expression,
        string edgeName,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        if (!TryRecord(expression, "AssetEdge", out Dictionary<string, BoundExpression> fields))
        {
            diagnostics.Add(AtRoot(source, sourcePath, "COPE-ASSET-0010", $"{edgeName} must resolve to an AssetEdge record."));
            return new ObjectAssetEdge([]);
        }

        IReadOnlyList<ObjectAssetEdgeSegment> segments = DecodeArray(
            Value(fields, "segments", source, sourcePath, diagnostics),
            item => DecodeSegment(item, source, sourcePath, diagnostics),
            source,
            sourcePath,
            diagnostics,
            edgeName + ".segments");
        return new ObjectAssetEdge(segments);
    }

    private static ObjectAssetEdgeSegment? DecodeSegment(
        BoundExpression expression,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        if (!TryRecord(expression, "AssetEdgeSegment", out Dictionary<string, BoundExpression> fields))
        {
            diagnostics.Add(AtRoot(source, sourcePath, "COPE-ASSET-0011", "Edge entries must resolve to AssetEdgeSegment records."));
            return null;
        }

        string allocation = String(fields, "allocation", source, sourcePath, diagnostics);
        SpanAllocationKind kind = allocation switch
        {
            "fixed" => SpanAllocationKind.Fixed,
            "flex" => SpanAllocationKind.Flex,
            _ => InvalidAllocation(allocation, source, sourcePath, diagnostics),
        };
        string samplingText = String(fields, "sampling", source, sourcePath, diagnostics);
        ObjectAssetSampling sampling = samplingText switch
        {
            "stretch" => ObjectAssetSampling.Stretch,
            "tile" => ObjectAssetSampling.Tile,
            "crop" => ObjectAssetSampling.Crop,
            _ => InvalidSampling(samplingText, source, sourcePath, diagnostics),
        };
        return new ObjectAssetEdgeSegment(
            String(fields, "id", source, sourcePath, diagnostics),
            String(fields, "region", source, sourcePath, diagnostics),
            kind,
            Int(fields, "length", source, sourcePath, diagnostics),
            Int(fields, "weight", source, sourcePath, diagnostics),
            sampling);
    }

    private static void Validate(
        ObjectAssetDocument document,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        if (document.SchemaVersion != 1)
        {
            diagnostics.Add(AtValue(source, sourcePath, document.SchemaVersion.ToString(CultureInfo.InvariantCulture), "COPE-ASSET-0100", "Object asset schemaVersion must be 1."));
        }

        ValidateId(document.Id, "asset", source, sourcePath, diagnostics);
        ValidateId(document.Texture.Id, "texture", source, sourcePath, diagnostics);
        if (document.Texture.Width <= 0 || document.Texture.Height <= 0)
        {
            diagnostics.Add(AtValue(source, sourcePath, document.Texture.Id, "COPE-ASSET-0101", "Texture dimensions must be positive."));
        }

        if (!IsSafeRelativePath(document.Texture.Source))
        {
            diagnostics.Add(AtValue(source, sourcePath, document.Texture.Source, "COPE-ASSET-0102", "Texture source must be a safe relative path."));
        }
        else
        {
            string texturePath = Path.GetFullPath(document.Texture.Source, Path.GetDirectoryName(sourcePath)!);
            if (!File.Exists(texturePath))
            {
                diagnostics.Add(AtValue(source, sourcePath, document.Texture.Source, "COPE-ASSET-0103", $"Referenced texture '{document.Texture.Source}' does not exist."));
            }
        }

        var regions = new Dictionary<string, ObjectAssetRegion>(StringComparer.Ordinal);
        foreach (ObjectAssetRegion region in document.Regions)
        {
            ValidateId(region.Id, "region", source, sourcePath, diagnostics);
            if (!regions.TryAdd(region.Id, region))
            {
                diagnostics.Add(AtValue(source, sourcePath, region.Id, "COPE-ASSET-0104", $"Duplicate stable region ID '{region.Id}'."));
            }

            if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0
                || (long)region.X + region.Width > document.Texture.Width
                || (long)region.Y + region.Height > document.Texture.Height)
            {
                diagnostics.Add(AtValue(source, sourcePath, region.Id, "COPE-ASSET-0105", $"Region '{region.Id}' is outside texture bounds."));
            }
        }

        var panelIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ObjectAssetPanel panel in document.Panels)
        {
            ValidateId(panel.Id, "panel", source, sourcePath, diagnostics);
            if (!panelIds.Add(panel.Id))
            {
                diagnostics.Add(AtValue(source, sourcePath, panel.Id, "COPE-ASSET-0106", $"Duplicate stable panel ID '{panel.Id}'."));
            }

            if (!double.IsFinite(panel.BorderScale) || panel.BorderScale <= 0)
            {
                diagnostics.Add(AtValue(source, sourcePath, panel.Id, "COPE-ASSET-0107", $"Panel '{panel.Id}' borderScale must be finite and positive."));
            }

            string[] requiredRegions =
            [
                panel.TopLeftRegionId,
                panel.TopRightRegionId,
                panel.BottomRightRegionId,
                panel.BottomLeftRegionId,
            ];
            if (panel.CenterPolicy != ObjectAssetCenterPolicy.AnalyticFill)
            {
                requiredRegions = [.. requiredRegions, panel.CenterRegionId];
            }

            foreach (string regionId in requiredRegions)
            {
                ValidateRegionReference(panel.Id, regionId, regions, source, sourcePath, diagnostics);
            }

            ValidateEdge(panel, "top", panel.Top, regions, source, sourcePath, diagnostics);
            ValidateEdge(panel, "right", panel.Right, regions, source, sourcePath, diagnostics);
            ValidateEdge(panel, "bottom", panel.Bottom, regions, source, sourcePath, diagnostics);
            ValidateEdge(panel, "left", panel.Left, regions, source, sourcePath, diagnostics);

            if (panel.ContentPadding.Left < 0 || panel.ContentPadding.Top < 0
                || panel.ContentPadding.Right < 0 || panel.ContentPadding.Bottom < 0)
            {
                diagnostics.Add(AtValue(source, sourcePath, panel.Id, "COPE-ASSET-0108", $"Panel '{panel.Id}' content padding must be non-negative."));
            }
        }
    }

    private static void ValidateEdge(
        ObjectAssetPanel panel,
        string edgeName,
        ObjectAssetEdge edge,
        IReadOnlyDictionary<string, ObjectAssetRegion> regions,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        if (edge.Segments.Count == 0)
        {
            diagnostics.Add(AtValue(source, sourcePath, panel.Id, "COPE-ASSET-0109", $"Panel '{panel.Id}' requires a non-empty {edgeName} edge."));
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var requests = new List<SpanAllocationRequest<string>>();
        foreach (ObjectAssetEdgeSegment segment in edge.Segments)
        {
            ValidateId(segment.Id, "edge segment", source, sourcePath, diagnostics);
            if (!ids.Add(segment.Id))
            {
                diagnostics.Add(AtValue(source, sourcePath, segment.Id, "COPE-ASSET-0110", $"Panel '{panel.Id}' {edgeName} edge has duplicate segment ID '{segment.Id}'."));
            }

            ValidateRegionReference(panel.Id, segment.RegionId, regions, source, sourcePath, diagnostics);
            requests.Add(segment.AllocationKind == SpanAllocationKind.Fixed
                ? SpanAllocationRequest<string>.Fixed(segment.Id, segment.MinimumLength)
                : SpanAllocationRequest<string>.Flex(segment.Id, segment.MinimumLength, segment.Weight));
        }

        SpanAllocationResult<string> validation = SpanAllocator.Resolve(edge.MinimumLength, requests);
        foreach (SpanAllocationDiagnostic diagnostic in validation.Diagnostics)
        {
            diagnostics.Add(AtValue(source, sourcePath, panel.Id, "COPE-ASSET-0111", $"Panel '{panel.Id}' {edgeName}: {diagnostic.Message}"));
        }
    }

    private static void ValidateRegionReference(
        string panelId,
        string regionId,
        IReadOnlyDictionary<string, ObjectAssetRegion> regions,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        if (!regions.ContainsKey(regionId))
        {
            diagnostics.Add(AtValue(source, sourcePath, regionId, "COPE-ASSET-0112", $"Panel '{panelId}' references missing region '{regionId}'."));
        }
    }

    private static void ValidateId(
        string id,
        string kind,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(id)
            || id.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
        {
            diagnostics.Add(AtValue(source, sourcePath, id, "COPE-ASSET-0113", $"{kind} ID '{id}' is invalid. Use letters, numbers, '.', '_' or '-'."));
        }
    }

    private static string EmitToml(ObjectAssetDocument document, string source, string sourceKind)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# GENERATED from " + source + "; edit the Copeland source, not this projection.");
        builder.AppendLine("schema_version = 1");
        builder.AppendLine("asset_id = " + Quote(document.Id));
        builder.AppendLine();
        builder.AppendLine("[atlas]");
        builder.AppendLine("image = " + Quote(document.Texture.Source));
        builder.AppendLine("width = " + document.Texture.Width.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("height = " + document.Texture.Height.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine($"source_kind = \"{sourceKind}\"");

        foreach (ObjectAssetRegion region in document.Regions.OrderBy(region => region.Id, StringComparer.Ordinal))
        {
            builder.AppendLine();
            builder.AppendLine("[regions." + Quote(region.Id) + "]");
            builder.AppendLine("x = " + region.X.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("y = " + region.Y.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("width = " + region.Width.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("height = " + region.Height.ToString(CultureInfo.InvariantCulture));
        }

        foreach (ObjectAssetPanel panel in document.Panels.OrderBy(panel => panel.Id, StringComparer.Ordinal))
        {
            IReadOnlyDictionary<string, ObjectAssetRegion> regions = document.Regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
            ObjectAssetRegion topLeft = regions[panel.TopLeftRegionId];
            ObjectAssetRegion topRight = regions[panel.TopRightRegionId];
            ObjectAssetRegion bottomRight = regions[panel.BottomRightRegionId];
            ObjectAssetRegion bottomLeft = regions[panel.BottomLeftRegionId];
            int panelLeft = Math.Min(topLeft.X, bottomLeft.X);
            int panelTop = Math.Min(topLeft.Y, topRight.Y);
            int panelRight = Math.Max(topRight.X + topRight.Width, bottomRight.X + bottomRight.Width);
            int panelBottom = Math.Max(bottomLeft.Y + bottomLeft.Height, bottomRight.Y + bottomRight.Height);
            builder.AppendLine();
            builder.AppendLine("# Compatibility projection for M14/static consumers.");
            builder.AppendLine("[ui_panels." + Quote(panel.Id) + "]");
            builder.AppendLine("x = " + panelLeft.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("y = " + panelTop.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("width = " + (panelRight - panelLeft).ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("height = " + (panelBottom - panelTop).ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("left = " + topLeft.Width.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("top = " + topLeft.Height.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("right = " + topRight.Width.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("bottom = " + bottomLeft.Height.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("edge_mode = \"stretch\"");
            builder.AppendLine("center_mode = \"stretch\"");
            builder.AppendLine("border_scale = " + panel.BorderScale.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine("extrusion = 0");
            builder.AppendLine();
            builder.AppendLine("[programmable_panels." + Quote(panel.Id) + "]");
            builder.AppendLine("top_left = " + Quote(panel.TopLeftRegionId));
            builder.AppendLine("top_right = " + Quote(panel.TopRightRegionId));
            builder.AppendLine("bottom_right = " + Quote(panel.BottomRightRegionId));
            builder.AppendLine("bottom_left = " + Quote(panel.BottomLeftRegionId));
            builder.AppendLine("center_policy = " + Quote(ToToml(panel.CenterPolicy)));
            builder.AppendLine("center_region = " + Quote(panel.CenterRegionId));
            builder.AppendLine("border_scale = " + panel.BorderScale.ToString("R", CultureInfo.InvariantCulture));
            builder.AppendLine("padding_left = " + panel.ContentPadding.Left.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("padding_top = " + panel.ContentPadding.Top.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("padding_right = " + panel.ContentPadding.Right.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("padding_bottom = " + panel.ContentPadding.Bottom.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("minimum_width = " + panel.MinimumWidth.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("minimum_height = " + panel.MinimumHeight.ToString(CultureInfo.InvariantCulture));
            AppendEdge(builder, panel.Id, "top", panel.Top);
            AppendEdge(builder, panel.Id, "right", panel.Right);
            AppendEdge(builder, panel.Id, "bottom", panel.Bottom);
            AppendEdge(builder, panel.Id, "left", panel.Left);
        }

        return builder.ToString();
    }

    private static void AppendEdge(StringBuilder builder, string panelId, string edgeName, ObjectAssetEdge edge)
    {
        foreach (ObjectAssetEdgeSegment segment in edge.Segments)
        {
            builder.AppendLine();
            builder.AppendLine("[[programmable_panels." + Quote(panelId) + "." + edgeName + "]]");
            builder.AppendLine("id = " + Quote(segment.Id));
            builder.AppendLine("region = " + Quote(segment.RegionId));
            builder.AppendLine("allocation = " + Quote(segment.AllocationKind == SpanAllocationKind.Fixed ? "fixed" : "flex"));
            builder.AppendLine("length = " + segment.MinimumLength.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("weight = " + segment.Weight.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("sampling = " + Quote(segment.Sampling.ToString().ToLowerInvariant()));
        }
    }

    private static string ToToml(ObjectAssetCenterPolicy policy)
    {
        return policy switch
        {
            ObjectAssetCenterPolicy.AnalyticFill => "analytic-fill",
            ObjectAssetCenterPolicy.StretchRegion => "stretch-region",
            ObjectAssetCenterPolicy.TileRegion => "tile-region",
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static ObjectAssetCenterPolicy InvalidCenterPolicy(string value, string source, string sourcePath, List<Diagnostic> diagnostics)
    {
        diagnostics.Add(AtValue(source, sourcePath, value, "COPE-ASSET-0012", $"Malformed center policy '{value}'."));
        return ObjectAssetCenterPolicy.AnalyticFill;
    }

    private static SpanAllocationKind InvalidAllocation(string value, string source, string sourcePath, List<Diagnostic> diagnostics)
    {
        diagnostics.Add(AtValue(source, sourcePath, value, "COPE-ASSET-0013", $"Invalid allocation policy '{value}'; expected 'fixed' or 'flex'."));
        return SpanAllocationKind.Fixed;
    }

    private static ObjectAssetSampling InvalidSampling(string value, string source, string sourcePath, List<Diagnostic> diagnostics)
    {
        diagnostics.Add(AtValue(source, sourcePath, value, "COPE-ASSET-0014", $"Malformed sampling mode '{value}'; expected 'stretch', 'tile', or 'crop'."));
        return ObjectAssetSampling.Stretch;
    }

    private static IReadOnlyList<T> DecodeArray<T>(
        BoundExpression? expression,
        Func<BoundExpression, T?> decode,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics,
        string name)
        where T : class
    {
        if (expression is not BoundArrayExpression array)
        {
            diagnostics.Add(AtRoot(source, sourcePath, "COPE-ASSET-0015", $"'{name}' must be a resolved immutable array."));
            return [];
        }

        return array.Elements.Select(decode).Where(item => item is not null).Cast<T>().ToArray();
    }

    private static bool TryRecord(
        BoundExpression? expression,
        string expectedName,
        out Dictionary<string, BoundExpression> fields)
    {
        fields = new Dictionary<string, BoundExpression>(StringComparer.Ordinal);
        if (expression is not BoundRecordConstructionExpression record
            || !string.Equals(record.RecordType.Name, expectedName, StringComparison.Ordinal))
        {
            return false;
        }

        fields = record.Initializers.ToDictionary(initializer => initializer.Field.Name, initializer => initializer.Value, StringComparer.Ordinal);
        return true;
    }

    private static BoundExpression? Value(
        IReadOnlyDictionary<string, BoundExpression> fields,
        string name,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        if (fields.TryGetValue(name, out BoundExpression? value))
        {
            return value;
        }

        diagnostics.Add(AtRoot(source, sourcePath, "COPE-ASSET-0016", $"Resolved asset record is missing field '{name}'."));
        return null;
    }

    private static string String(
        IReadOnlyDictionary<string, BoundExpression> fields,
        string name,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        return Value(fields, name, source, sourcePath, diagnostics) is BoundLiteralExpression { Value: string value }
            ? value
            : InvalidPrimitive<string>(name, "string", source, sourcePath, diagnostics);
    }

    private static int Int(
        IReadOnlyDictionary<string, BoundExpression> fields,
        string name,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        return Value(fields, name, source, sourcePath, diagnostics) is BoundLiteralExpression { Value: int value }
            ? value
            : InvalidPrimitive<int>(name, "int", source, sourcePath, diagnostics);
    }

    private static double Number(
        IReadOnlyDictionary<string, BoundExpression> fields,
        string name,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        BoundExpression? expression = Value(fields, name, source, sourcePath, diagnostics);
        return expression switch
        {
            BoundLiteralExpression { Value: double value } => value,
            BoundLiteralExpression { Value: int value } => value,
            _ => InvalidPrimitive<double>(name, "number", source, sourcePath, diagnostics),
        };
    }

    private static T InvalidPrimitive<T>(
        string name,
        string expected,
        string source,
        string sourcePath,
        List<Diagnostic> diagnostics)
    {
        diagnostics.Add(AtRoot(source, sourcePath, "COPE-ASSET-0017", $"Resolved field '{name}' must be a {expected}."));
        return default!;
    }

    private static bool IsSafeRelativePath(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !Path.IsPathRooted(value)
            && !value.Split('/', '\\').Contains("..", StringComparer.Ordinal);
    }

    private static Diagnostic AtRoot(string source, string sourcePath, string id, string message)
    {
        int position = source.IndexOf(RootBindingName, StringComparison.Ordinal);
        return new Diagnostic(id, message, Math.Max(0, position), Math.Max(1, RootBindingName.Length), sourcePath);
    }

    private static Diagnostic AtValue(string source, string sourcePath, string value, string id, string message)
    {
        int position = string.IsNullOrEmpty(value) ? -1 : source.IndexOf(value, StringComparison.Ordinal);
        return new Diagnostic(id, message, Math.Max(0, position), Math.Max(1, value.Length), sourcePath);
    }

    private static ObjectAssetCompilationResult Failure(Diagnostic diagnostic)
    {
        return new ObjectAssetCompilationResult(null, [diagnostic]);
    }
}
