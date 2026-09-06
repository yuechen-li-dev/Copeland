using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Copeland.SpanAllocation;
using Copeland.TS.Assets;
using Oblivion.Model;

namespace Oblivion.App;

/// <summary>
/// App-owned orchestration for compiler-authoritative graphical assets.
/// Cards are rebuilt projections; no card value is persisted as asset truth.
/// </summary>
public sealed class OblivionSpriteCardService
{
    private long _compileVersion;

    public SpriteCardProjection BuildProjection(
        string sourcePath,
        string panelId,
        int width,
        int height)
    {
        Stopwatch timer = Stopwatch.StartNew();
        string fullPath = Path.GetFullPath(sourcePath);
        string source = File.ReadAllText(fullPath);
        string hash = Hash(source);
        ObjectAssetCompilationResult compilation = ObjectAssetCompiler.Compile(source, fullPath);
        if (!compilation.Success || compilation.Document is null)
        {
            IReadOnlyList<SpriteCardDiagnostic> failureDiagnostics = compilation.Diagnostics
                .Select(diagnostic => new SpriteCardDiagnostic(
                    diagnostic.Id,
                    SpriteCardDiagnosticSeverity.Error,
                    diagnostic.Message))
                .ToArray();
            return new SpriteCardProjection(
                "<unresolved>",
                panelId,
                fullPath,
                string.Empty,
                0,
                0,
                hash,
                ++_compileVersion,
                width,
                height,
                [],
                [],
                failureDiagnostics,
                timer.Elapsed);
        }

        ObjectAssetDocument document = compilation.Document;
        ObjectAssetPanel panel = document.Panels.SingleOrDefault(candidate => candidate.Id == panelId)
            ?? throw new ArgumentException($"Panel '{panelId}' does not exist in '{sourcePath}'.", nameof(panelId));
        IReadOnlyDictionary<string, ObjectAssetRegion> regions = document.Regions.ToDictionary(
            region => region.Id,
            StringComparer.Ordinal);
        var cards = new List<SpriteCard>();
        var diagnostics = new List<SpriteCardDiagnostic>();
        var summaries = new List<SpriteCardEdgeSummary>();
        GraphicalConceptPath panelPath = new("panel." + panel.Id);
        cards.Add(new SpriteCard(
            panelPath,
            GraphicalConceptKind.Panel,
            "programmable-panel",
            LocateText(fullPath, source, Quote(panel.Id)),
            null,
            new SpriteCardAuthoredState($"center={panel.CenterPolicy}; borderScale={panel.BorderScale:R}"),
            new SpriteCardResolvedState(null, null, new GraphicalRect(0, 0, width, height), "resolved"),
            new SpriteCardRuntimeState(true, "SpriteForge programmable panel"),
            [],
            [],
            []));

        foreach (ObjectAssetRegion region in document.Regions)
        {
            GraphicalConceptPath path = new("region." + region.Id);
            cards.Add(new SpriteCard(
                path,
                GraphicalConceptKind.Region,
                "atlas-region",
                LocateText(fullPath, source, Quote(region.Id)),
                new GraphicalRect(region.X, region.Y, region.Width, region.Height),
                new SpriteCardAuthoredState("record table AssetRegions", RegionId: region.Id),
                null,
                new SpriteCardRuntimeState(true, "SpriteForge atlas region"),
                [new SpriteCardRelationship(SpriteCardRelationshipKind.SourceOf, panelPath)],
                [],
                []));
        }

        int horizontalExtent = ComputeHorizontalExtent(panel, regions, width);
        int verticalExtent = ComputeVerticalExtent(panel, regions, height);
        AddEdge("top", panel.Top, horizontalExtent);
        AddEdge("right", panel.Right, verticalExtent);
        AddEdge("bottom", panel.Bottom, horizontalExtent);
        AddEdge("left", panel.Left, verticalExtent);

        foreach (ObjectAssetAuthoringConcept concept in document.AuthoredConcepts)
        {
            GraphicalConceptPath path = new(concept.Path);
            GraphicalConceptKind kind = concept.Kind switch
            {
                ObjectAssetAuthoringConceptKind.Guide => GraphicalConceptKind.Guide,
                ObjectAssetAuthoringConceptKind.Datum => GraphicalConceptKind.Datum,
                ObjectAssetAuthoringConceptKind.Blockout => GraphicalConceptKind.Blockout,
                _ => throw new ArgumentOutOfRangeException(),
            };
            cards.Add(new SpriteCard(
                path,
                kind,
                concept.Axis == "none" ? kind.ToString().ToLowerInvariant() : concept.Axis,
                LocateText(fullPath, source, Quote(concept.Path)),
                null,
                new SpriteCardAuthoredState($"visible={concept.Visible}; axis={concept.Axis}"),
                new SpriteCardResolvedState(
                    null,
                    null,
                    new GraphicalRect(concept.X, concept.Y, concept.Width, concept.Height),
                    "authoring-only"),
                new SpriteCardRuntimeState(false, "erased before runtime TOML"),
                [new SpriteCardRelationship(SpriteCardRelationshipKind.AttachedTo, panelPath)],
                [],
                [SpriteCardEditProperty.GuideVisibility]));
        }

        timer.Stop();
        return new SpriteCardProjection(
            document.Id,
            panel.Id,
            fullPath,
            Path.GetFullPath(document.Texture.Source, Path.GetDirectoryName(fullPath)!),
            document.Texture.Width,
            document.Texture.Height,
            hash,
            ++_compileVersion,
            width,
            height,
            cards,
            summaries,
            diagnostics,
            timer.Elapsed);

        void AddEdge(string edgeName, ObjectAssetEdge edge, int extent)
        {
            SpanAllocationRequest<ObjectAssetEdgeSegment>[] requests = edge.Segments
                .Select(segment => segment.AllocationKind == SpanAllocationKind.Fixed
                    ? SpanAllocationRequest<ObjectAssetEdgeSegment>.Fixed(segment, segment.MinimumLength)
                    : SpanAllocationRequest<ObjectAssetEdgeSegment>.Flex(segment, segment.MinimumLength, segment.Weight))
                .ToArray();
            SpanAllocationResult<ObjectAssetEdgeSegment> allocation = SpanAllocator.Resolve(extent, requests);
            summaries.Add(new SpriteCardEdgeSummary(
                edgeName,
                allocation.Extent,
                allocation.MinimumDemand,
                allocation.UsedLength,
                allocation.UnusedLength,
                allocation.DeficitLength,
                FormatStatus(allocation.Status)));

            foreach (SpanAllocationDiagnostic diagnostic in allocation.Diagnostics)
            {
                diagnostics.Add(new SpriteCardDiagnostic(
                    diagnostic.Code,
                    diagnostic.Code.EndsWith("0100", StringComparison.Ordinal)
                        ? SpriteCardDiagnosticSeverity.Warning
                        : SpriteCardDiagnosticSeverity.Error,
                    $"{edgeName}: {diagnostic.Message}",
                    panelPath));
            }

            foreach (SpanPlacement<ObjectAssetEdgeSegment> placement in allocation.Placements)
            {
                ObjectAssetEdgeSegment segment = placement.Payload;
                GraphicalConceptPath conceptPath = SegmentPath(panel.Id, segment.Id);
                ObjectAssetRegion region = regions[segment.RegionId];
                GraphicalSourceLocation location = segment.AllocationKind == SpanAllocationKind.Flex
                    ? LocateSegmentProperty(
                        fullPath,
                        source,
                        segment,
                        SpriteCardEditProperty.SourceRegion)
                    : LocateText(fullPath, source, Quote(segment.RegionId));
                IReadOnlyList<SpriteCardEditProperty> capabilities = segment.AllocationKind == SpanAllocationKind.Flex
                    ? [
                        SpriteCardEditProperty.FlexWeight,
                        SpriteCardEditProperty.MinimumLength,
                        SpriteCardEditProperty.Sampling,
                        SpriteCardEditProperty.SourceRegion,
                    ]
                    : [];
                cards.Add(new SpriteCard(
                    conceptPath,
                    GraphicalConceptKind.EdgeSegment,
                    edgeName,
                    location,
                    new GraphicalRect(region.X, region.Y, region.Width, region.Height),
                    new SpriteCardAuthoredState(
                        segment.AllocationKind == SpanAllocationKind.Fixed ? "fixed" : "flex",
                        segment.MinimumLength,
                        segment.Weight,
                        segment.Sampling.ToString().ToLowerInvariant(),
                        segment.RegionId),
                    new SpriteCardResolvedState(
                        placement.Offset,
                        placement.Length,
                        null,
                        FormatStatus(allocation.Status)),
                    new SpriteCardRuntimeState(true, "allocator placement -> Machina quad"),
                    [
                        new SpriteCardRelationship(SpriteCardRelationshipKind.Parent, panelPath),
                        new SpriteCardRelationship(
                            SpriteCardRelationshipKind.SourceOf,
                            new GraphicalConceptPath("region." + segment.RegionId)),
                    ],
                    diagnostics.Where(item => item.ConceptPath == conceptPath).ToArray(),
                    capabilities));
            }
        }
    }

    public SpriteCardEditTrace ApplyEdit(
        SpriteCardProjection projection,
        GraphicalConceptPath conceptPath,
        SpriteCardEditProperty property,
        string after)
    {
        ArgumentNullException.ThrowIfNull(projection);
        string source = File.ReadAllText(projection.SourcePath);
        string currentHash = Hash(source);
        SpriteCard? card = projection.Cards.SingleOrDefault(candidate => candidate.ConceptPath == conceptPath);
        if (card is null)
        {
            return Reject("OBLIVION-SPRITE-CARD-TARGET-MISSING", $"Concept '{conceptPath}' is not in the projection.", currentHash);
        }

        if (!string.Equals(currentHash, projection.SourceSha256, StringComparison.Ordinal))
        {
            return Reject(
                "OBLIVION-SPRITE-CARD-STALE-SOURCE",
                "The authored source changed after this card projection was built; refresh before editing.",
                currentHash);
        }

        if (!card.EditCapabilities.Contains(property))
        {
            return Reject("OBLIVION-SPRITE-CARD-EDIT-UNSUPPORTED", $"'{property}' is not editable for '{conceptPath}'.", currentHash);
        }

        ObjectAssetCompilationResult initial = ObjectAssetCompiler.Compile(source, projection.SourcePath);
        ObjectAssetEdgeSegment segment = FindSegment(initial.Document, projection.PanelId, conceptPath);
        GraphicalSourceLocation span = LocateSegmentProperty(projection.SourcePath, source, segment, property);
        string before = source.Substring(span.Start, span.Length);
        string replacement = NormalizeReplacement(property, after);
        string candidate = source[..span.Start] + replacement + source[(span.Start + span.Length)..];
        Stopwatch timer = Stopwatch.StartNew();
        ObjectAssetCompilationResult compiled = ObjectAssetCompiler.Compile(candidate, projection.SourcePath);
        if (!compiled.Success || compiled.Document is null)
        {
            timer.Stop();
            IReadOnlyList<SpriteCardDiagnostic> compileDiagnostics = compiled.Diagnostics.Select(diagnostic =>
                new SpriteCardDiagnostic(
                    diagnostic.Id,
                    SpriteCardDiagnosticSeverity.Error,
                    diagnostic.Message,
                    conceptPath)).ToArray();
            return Trace(false, "compile-failed", before, replacement, span, currentHash, currentHash, timer.Elapsed, compileDiagnostics);
        }

        string temporaryPath = projection.SourcePath + ".m16.tmp";
        try
        {
            File.WriteAllText(temporaryPath, candidate, new UTF8Encoding(false));
            File.Move(temporaryPath, projection.SourcePath, overwrite: true);
            EmitOutputs(compiled.Document, projection.SourcePath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        timer.Stop();
        string nextHash = Hash(candidate);
        return Trace(true, "success", before, replacement, span, currentHash, nextHash, timer.Elapsed, []);

        SpriteCardEditTrace Reject(string code, string message, string observedHash)
        {
            GraphicalSourceLocation fallback = card?.Source ?? new GraphicalSourceLocation(
                projection.SourcePath, 0, 1, 1, 1);
            return Trace(
                false,
                "rejected",
                card?.Authored.Policy ?? string.Empty,
                after,
                fallback,
                projection.SourceSha256,
                observedHash,
                TimeSpan.Zero,
                [new SpriteCardDiagnostic(code, SpriteCardDiagnosticSeverity.Error, message, conceptPath)]);
        }

        SpriteCardEditTrace Trace(
            bool applied,
            string compileResult,
            string beforeValue,
            string afterValue,
            GraphicalSourceLocation sourceLocation,
            string hashBefore,
            string hashAfter,
            TimeSpan duration,
            IReadOnlyList<SpriteCardDiagnostic> traceDiagnostics)
        {
            SpriteCardEditIntent intent = new(
                conceptPath,
                property,
                beforeValue,
                afterValue,
                sourceLocation,
                projection.SourceSha256);
            return new SpriteCardEditTrace(
                intent,
                applied,
                compileResult,
                hashBefore,
                hashAfter,
                duration,
                traceDiagnostics);
        }
    }

    private static ObjectAssetEdgeSegment FindSegment(
        ObjectAssetDocument? document,
        string panelId,
        GraphicalConceptPath conceptPath)
    {
        ObjectAssetPanel panel = document?.Panels.Single(candidate => candidate.Id == panelId)
            ?? throw new InvalidOperationException("The authoritative asset did not compile.");
        return new[] { panel.Top, panel.Right, panel.Bottom, panel.Left }
            .SelectMany(edge => edge.Segments)
            .Single(segment => SegmentPath(panelId, segment.Id) == conceptPath);
    }

    private static GraphicalConceptPath SegmentPath(string panelId, string segmentId)
    {
        string local = segmentId.StartsWith(panelId + ".", StringComparison.Ordinal)
            ? segmentId
            : panelId + "." + segmentId;
        return new GraphicalConceptPath("panel." + local);
    }

    private static int ComputeHorizontalExtent(
        ObjectAssetPanel panel,
        IReadOnlyDictionary<string, ObjectAssetRegion> regions,
        int width)
    {
        double corners = (regions[panel.TopLeftRegionId].Width + regions[panel.TopRightRegionId].Width)
            * panel.BorderScale;
        return Math.Max(0, (int)Math.Round(width - corners));
    }

    private static int ComputeVerticalExtent(
        ObjectAssetPanel panel,
        IReadOnlyDictionary<string, ObjectAssetRegion> regions,
        int height)
    {
        double corners = (regions[panel.TopLeftRegionId].Height + regions[panel.BottomLeftRegionId].Height)
            * panel.BorderScale;
        return Math.Max(0, (int)Math.Round(height - corners));
    }

    private static string FormatStatus(SpanAllocationStatus status)
    {
        return status.ToString()[..1].ToLowerInvariant() + status.ToString()[1..];
    }

    private static string NormalizeReplacement(SpriteCardEditProperty property, string value)
    {
        return property switch
        {
            SpriteCardEditProperty.FlexWeight or SpriteCardEditProperty.MinimumLength
                when int.TryParse(value, out int number) && number >= 0 => number.ToString(),
            SpriteCardEditProperty.Sampling when value is "stretch" or "tile" or "crop" => Quote(value),
            SpriteCardEditProperty.SourceRegion when GraphicalConceptPath.TryCreate("region." + value, out _) => Quote(value),
            _ => throw new ArgumentException($"Value '{value}' is invalid for {property}.", nameof(value)),
        };
    }

    private static GraphicalSourceLocation LocateSegmentProperty(
        string path,
        string source,
        ObjectAssetEdgeSegment segment,
        SpriteCardEditProperty property)
    {
        string[] idParts = segment.Id.Split('.');
        string edgePrefix = string.Join('.', idParts.Take(idParts.Length - 1));
        string role = idParts[^1];
        string callName = edgePrefix.EndsWith(".top", StringComparison.Ordinal)
            || edgePrefix.EndsWith(".bottom", StringComparison.Ordinal)
                ? "horizontalEdge"
                : "verticalEdge";
        IReadOnlyList<SourceArgument> arguments = FindCallArguments(source, callName, edgePrefix);
        int argumentIndex = ResolveArgumentIndex(role, property);
        SourceArgument argument = arguments[argumentIndex];
        return Location(path, source, argument.Start, argument.Length);
    }

    private static int ResolveArgumentIndex(string role, SpriteCardEditProperty property)
    {
        bool center = role == "center";
        bool glow = role.StartsWith("glow-", StringComparison.Ordinal);
        if (!center && !glow)
        {
            throw new InvalidOperationException($"M16 source edits are bounded to flex center/glow segments, not '{role}'.");
        }

        return property switch
        {
            SpriteCardEditProperty.SourceRegion => center ? 4 : 3,
            SpriteCardEditProperty.MinimumLength => center ? 8 : 6,
            SpriteCardEditProperty.FlexWeight => center ? 9 : 7,
            SpriteCardEditProperty.Sampling => center ? 11 : 10,
            _ => throw new InvalidOperationException($"M16 does not source-edit '{property}' through an edge call."),
        };
    }

    private static IReadOnlyList<SourceArgument> FindCallArguments(
        string source,
        string callName,
        string firstStringArgument)
    {
        int search = 0;
        while ((search = source.IndexOf(callName + "(", search, StringComparison.Ordinal)) >= 0)
        {
            int open = search + callName.Length;
            IReadOnlyList<SourceArgument> arguments = SplitArguments(source, open);
            if (arguments.Count > 0 && Unquote(source.Substring(arguments[0].Start, arguments[0].Length)) == firstStringArgument)
            {
                return arguments;
            }

            search = open + 1;
        }

        throw new InvalidOperationException($"Could not locate authored {callName} call for '{firstStringArgument}'.");
    }

    private static IReadOnlyList<SourceArgument> SplitArguments(string source, int openParenthesis)
    {
        var result = new List<SourceArgument>();
        int depth = 0;
        int start = openParenthesis + 1;
        bool inString = false;
        bool escaped = false;
        for (int index = start; index < source.Length; index++)
        {
            char character = source[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
            }
            else if (character == '(')
            {
                depth++;
            }
            else if (character == ')' && depth == 0)
            {
                AddArgument(start, index);
                return result;
            }
            else if (character == ')')
            {
                depth--;
            }
            else if (character == ',' && depth == 0)
            {
                AddArgument(start, index);
                start = index + 1;
            }
        }

        throw new InvalidOperationException("Unterminated authored edge call.");

        void AddArgument(int rawStart, int rawEnd)
        {
            while (rawStart < rawEnd && char.IsWhiteSpace(source[rawStart]))
            {
                rawStart++;
            }

            while (rawEnd > rawStart && char.IsWhiteSpace(source[rawEnd - 1]))
            {
                rawEnd--;
            }

            result.Add(new SourceArgument(rawStart, rawEnd - rawStart));
        }
    }

    private static GraphicalSourceLocation LocateText(string path, string source, string text)
    {
        int start = source.IndexOf(text, StringComparison.Ordinal);
        return Location(path, source, Math.Max(0, start), Math.Max(1, text.Length));
    }

    private static GraphicalSourceLocation Location(string path, string source, int start, int length)
    {
        int line = 1;
        int column = 1;
        for (int index = 0; index < start; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return new GraphicalSourceLocation(path, start, length, line, column);
    }

    private static void EmitOutputs(ObjectAssetDocument document, string sourcePath)
    {
        ObjectAssetBuildOutputs outputs = ObjectAssetCompiler.Emit(document, sourcePath);
        string stem = sourcePath[..^".obj.ts".Length];
        File.WriteAllText(stem + ".obj.toml", outputs.Toml);
        File.WriteAllText(stem + ".runtime.toml", outputs.RuntimeToml);
        File.WriteAllText(stem + ".obj.json", outputs.Json);
        File.WriteAllText(stem + ".audit.json", outputs.AuditJson);
    }

    private static string Hash(string source)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }

    private static string Quote(string value)
    {
        return "\"" + value + "\"";
    }

    private static string Unquote(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }

    private readonly record struct SourceArgument(int Start, int Length);
}
