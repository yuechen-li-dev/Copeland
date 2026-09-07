using System.Diagnostics;
using System.Text.RegularExpressions;
using Copeland.TS.Assets;
using Oblivion.Model;

namespace Oblivion.App;

public sealed partial class OblivionSpriteCardService
{
    private readonly List<SpriteCardStructuralEditResult> _structuralHistory = [];

    public IReadOnlyList<SpriteCardStructuralEditResult> StructuralHistory => _structuralHistory;

    public SpriteCardStructuralEditResult ApplyStructuralEdit(
        SpriteCardProjection projection,
        SpriteCardStructuralEditIntent intent)
    {
        return ExecuteStructuralEdit(projection, intent, commit: true);
    }

    public SpriteCardStructuralEditResult PreviewStructuralEdit(
        SpriteCardProjection projection,
        SpriteCardStructuralEditIntent intent)
    {
        return ExecuteStructuralEdit(projection, intent, commit: false);
    }

    private SpriteCardStructuralEditResult ExecuteStructuralEdit(
        SpriteCardProjection projection,
        SpriteCardStructuralEditIntent intent,
        bool commit)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(intent);

        string source = File.ReadAllText(projection.SourcePath);
        string currentHash = Hash(source);
        GraphicalConceptPath? targetPath = ResolveTargetPath(intent);
        if (!string.Equals(currentHash, projection.SourceSha256, StringComparison.Ordinal) ||
            !string.Equals(currentHash, intent.ExpectedSourceSha256, StringComparison.Ordinal))
        {
            return Reject(
                "OBLIVION-SPRITE-CARD-STALE-SOURCE",
                "The authored source changed after this structural intent was created; refresh before editing.");
        }

        if (!TryResolveExpectedConstruct(
                intent.ParentPath,
                projection.PanelId,
                out string expectedConstruct,
                out string error))
        {
            return Reject("OBLIVION-SPRITE-CARD-STRUCTURAL-TARGET", error);
        }

        if (!string.Equals(
                intent.ExpectedEnclosingConstructIdentity,
                expectedConstruct,
                StringComparison.Ordinal))
        {
            return Reject(
                "OBLIVION-SPRITE-CARD-ENCLOSING-MISMATCH",
                $"Expected enclosing construct '{intent.ExpectedEnclosingConstructIdentity}', but '{intent.ParentPath}' is authored by '{expectedConstruct}'.");
        }

        if (!TryFindOrderedProgram(source, expectedConstruct, out OrderedProgram program, out error))
        {
            return Reject("OBLIVION-SPRITE-CARD-AMBIGUOUS-SOURCE", error);
        }

        ObjectAssetCompilationResult initial = ObjectAssetCompiler.Compile(source, projection.SourcePath);
        if (!initial.Success || initial.Document is null)
        {
            return Reject(
                "OBLIVION-SPRITE-CARD-SOURCE-INVALID",
                "The authoritative source must compile before a structural edit can be applied.");
        }

        if (!TryResolveEdge(intent.ParentPath, projection.PanelId, initial.Document, out EdgeTarget edge, out error))
        {
            return Reject("OBLIVION-SPRITE-CARD-STRUCTURAL-TARGET", error);
        }

        Stopwatch transformTimer = Stopwatch.StartNew();
        if (!TryTransform(
                source,
                initial.Document,
                edge,
                program,
                intent,
                targetPath,
                out string candidate,
                out error))
        {
            transformTimer.Stop();
            return Reject("OBLIVION-SPRITE-CARD-STRUCTURAL-INVALID", error, transformTimer.Elapsed);
        }

        transformTimer.Stop();
        Stopwatch compileTimer = Stopwatch.StartNew();
        ObjectAssetCompilationResult compiled = ObjectAssetCompiler.Compile(candidate, projection.SourcePath);
        compileTimer.Stop();
        if (!compiled.Success || compiled.Document is null)
        {
            IReadOnlyList<SpriteCardDiagnostic> diagnostics = compiled.Diagnostics.Select(diagnostic =>
                new SpriteCardDiagnostic(
                    diagnostic.Id,
                    SpriteCardDiagnosticSeverity.Error,
                    diagnostic.Message,
                    targetPath)).ToArray();
            return Result(
                applied: false,
                status: "compile-failed",
                candidate,
                program,
                currentHash,
                currentHash,
                diagnostics,
                "compile-failed",
                transformTimer.Elapsed,
                compileTimer.Elapsed,
                null);
        }

        if (!ValidateSemanticResult(
                initial.Document,
                compiled.Document,
                edge,
                intent,
                targetPath,
                out error))
        {
            return Reject(
                "OBLIVION-SPRITE-CARD-SEMANTIC-VALIDATION",
                error,
                transformTimer.Elapsed,
                compileTimer.Elapsed);
        }

        Stopwatch refreshTimer = Stopwatch.StartNew();
        SpriteCardProjection refreshed;
        try
        {
            refreshed = BuildProjectionFromSource(
                projection.SourcePath,
                candidate,
                projection.PanelId,
                projection.Width,
                projection.Height);
        }
        catch (Exception exception)
        {
            refreshTimer.Stop();
            return Reject(
                "OBLIVION-SPRITE-CARD-REFRESH-FAILED",
                $"The validated candidate could not be projected and was not committed: {exception.Message}",
                transformTimer.Elapsed,
                compileTimer.Elapsed);
        }

        refreshTimer.Stop();
        if (refreshed.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == SpriteCardDiagnosticSeverity.Error))
        {
            return Result(
                applied: false,
                status: "refresh-failed",
                candidate,
                program,
                currentHash,
                currentHash,
                refreshed.Diagnostics,
                "success",
                transformTimer.Elapsed,
                compileTimer.Elapsed,
                null,
                refreshTimer.Elapsed);
        }

        if (commit)
        {
            try
            {
                CommitSourceAndOutputs(projection.SourcePath, candidate, compiled.Document, "m17");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Reject(
                    "OBLIVION-SPRITE-CARD-WRITE-FAILED",
                    $"The validated structural edit could not be committed and was rolled back: {exception.Message}",
                    transformTimer.Elapsed,
                    compileTimer.Elapsed);
            }
        }

        string nextHash = Hash(candidate);
        SpriteCardStructuralEditResult result = Result(
            applied: commit,
            status: commit ? "success" : "preview",
            candidate,
            program,
            currentHash,
            nextHash,
            refreshed.Diagnostics,
            "success",
            transformTimer.Elapsed,
            compileTimer.Elapsed,
            refreshed,
            refreshTimer.Elapsed);
        if (commit)
        {
            _structuralHistory.Add(result);
        }
        return result;

        SpriteCardStructuralEditResult Reject(
            string code,
            string message,
            TimeSpan transformationDuration = default,
            TimeSpan compileDuration = default)
        {
            var diagnostic = new SpriteCardDiagnostic(
                code,
                SpriteCardDiagnosticSeverity.Error,
                message,
                targetPath);
            return new SpriteCardStructuralEditResult(
                false,
                "rejected",
                intent.Kind,
                targetPath,
                intent.AnchorPath,
                null,
                null,
                projection.SourceSha256,
                currentHash,
                string.Empty,
                string.Empty,
                [],
                [],
                [],
                [diagnostic],
                "not-run",
                transformationDuration,
                compileDuration,
                TimeSpan.Zero,
                TimeSpan.Zero,
                null,
                WouldApply: false,
                CommitStatus: "rejected");
        }

        SpriteCardStructuralEditResult Result(
            bool applied,
            string status,
            string candidateSource,
            OrderedProgram sourceProgram,
            string hashBefore,
            string hashAfter,
            IReadOnlyList<SpriteCardDiagnostic> diagnostics,
            string compileResult,
            TimeSpan transformationDuration,
            TimeSpan compileDuration,
            SpriteCardProjection? refreshedProjection,
            TimeSpan cardRefreshDuration = default)
        {
            IReadOnlyList<GraphicalConceptPath> beforePaths = PanelPaths(initial.Document, projection.PanelId);
            IReadOnlyList<GraphicalConceptPath> afterPaths = compiled.Document is null
                ? beforePaths
                : PanelPaths(compiled.Document, projection.PanelId);
            IReadOnlyList<GraphicalConceptPath> added = afterPaths.Except(beforePaths).ToArray();
            IReadOnlyList<GraphicalConceptPath> removed = beforePaths.Except(afterPaths).ToArray();
            IReadOnlyList<GraphicalConceptPath> moved = intent.Kind is
                SpriteCardStructuralEditKind.MoveBefore or SpriteCardStructuralEditKind.MoveAfter && targetPath is not null
                    ? [targetPath.Value]
                    : [];
            int beforeLength = sourceProgram.CloseBracket - sourceProgram.OpenBracket + 1;
            int afterLength = candidateSource.Length - source.Length + beforeLength;
            IReadOnlyList<GraphicalConceptPath> affected = added
                .Concat(removed)
                .Concat(moved)
                .Distinct()
                .ToArray();
            IReadOnlyList<string> fanout = affected
                .Select(path => path.Value.Split('.'))
                .Where(parts => parts.Length >= 3)
                .Select(parts => parts[2])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(edgeName => edgeName, StringComparer.Ordinal)
                .ToArray();
            return new SpriteCardStructuralEditResult(
                applied,
                status,
                intent.Kind,
                targetPath,
                intent.AnchorPath,
                Location(projection.SourcePath, source, sourceProgram.OpenBracket, beforeLength),
                Location(projection.SourcePath, candidateSource, sourceProgram.OpenBracket, afterLength),
                hashBefore,
                hashAfter,
                source.Substring(sourceProgram.OpenBracket, beforeLength),
                candidateSource.Substring(sourceProgram.OpenBracket, afterLength),
                added,
                removed,
                moved,
                diagnostics,
                compileResult,
                transformationDuration,
                compileDuration,
                cardRefreshDuration,
                TimeSpan.Zero,
                refreshedProjection,
                WouldApply: refreshedProjection is not null,
                CommitStatus: commit ? "committed" : "preview-only",
                FanoutEdges: fanout,
                AffectedRuntimeProjections: affected,
                BeforeAllocation: projection.EdgeSummaries,
                AfterAllocation: refreshedProjection?.EdgeSummaries ?? []);
        }
    }

    private static bool TryTransform(
        string source,
        ObjectAssetDocument document,
        EdgeTarget edge,
        OrderedProgram program,
        SpriteCardStructuralEditIntent intent,
        GraphicalConceptPath? targetPath,
        out string candidate,
        out string error)
    {
        candidate = source;
        error = string.Empty;
        var items = program.Items.ToList();
        string? targetLocal = targetPath is null ? null : LocalName(targetPath.Value);
        string? anchorLocal = intent.AnchorPath is null ? null : LocalName(intent.AnchorPath.Value);
        bool targetAmbiguous = false;
        bool anchorAmbiguous = false;
        int targetIndex = targetLocal is null ? -1 : UniqueIndex(items, targetLocal, out targetAmbiguous);
        int anchorIndex = anchorLocal is null ? -1 : UniqueIndex(items, anchorLocal, out anchorAmbiguous);
        if (targetAmbiguous || anchorAmbiguous)
        {
            error = $"Multiple source entries match target '{targetPath}' or anchor '{intent.AnchorPath}'.";
            return false;
        }

        if (intent.Kind is SpriteCardStructuralEditKind.InsertBefore or SpriteCardStructuralEditKind.InsertAfter)
        {
            if (intent.NewConcept is null || intent.AnchorPath is null || anchorIndex < 0)
            {
                error = "Insertion requires one locatable anchor and a new segment definition.";
                return false;
            }

            if (items.Any(item => item.LocalId == intent.NewConcept.LocalId))
            {
                error = $"Concept local ID '{intent.NewConcept.LocalId}' already exists in the authored program.";
                return false;
            }

            if (!GraphicalConceptPath.TryCreate(intent.ParentPath.Value + "." + intent.NewConcept.LocalId, out _))
            {
                error = $"Concept local ID '{intent.NewConcept.LocalId}' does not form a valid Concept Path.";
                return false;
            }

            if (!TryResolveRegionExpression(source, document, edge, items, intent.NewConcept.RegionId, out string regionExpression))
            {
                error = $"Region '{intent.NewConcept.RegionId}' is not represented by this authored edge template.";
                return false;
            }

            if (!TryCreateSegmentExpression(intent.NewConcept, regionExpression, out string expression, out error))
            {
                return false;
            }

            SourceItem anchor = items[anchorIndex];
            string newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string indentation = LineIndentation(source, anchor.ExpressionStart);
            string raw = newline + indentation + expression + ",";
            var inserted = new SourceItem(0, 0, 0, 0, intent.NewConcept.LocalId, raw);
            int insertionIndex = intent.Kind == SpriteCardStructuralEditKind.InsertBefore
                ? anchorIndex
                : anchorIndex + 1;
            items.Insert(insertionIndex, inserted);
        }
        else if (intent.Kind == SpriteCardStructuralEditKind.Remove)
        {
            if (targetIndex < 0)
            {
                error = $"Concept '{targetPath}' is not uniquely locatable in the authored program.";
                return false;
            }

            if (edge.Segments.Segments.Count <= 3 || targetIndex == 0 || targetIndex == items.Count - 1)
            {
                error = "Removal must leave at least three segments and cannot remove an edge cap.";
                return false;
            }

            SourceItem removed = items[targetIndex];
            string retainedTrivia = source.Substring(
                removed.RawStart,
                removed.ExpressionStart - removed.RawStart);
            if (!retainedTrivia.Contains("//", StringComparison.Ordinal) &&
                !retainedTrivia.Contains("/*", StringComparison.Ordinal))
            {
                retainedTrivia = string.Empty;
            }

            items[targetIndex] = removed with { LocalId = null, RawText = retainedTrivia };
        }
        else
        {
            if (targetIndex < 0 || anchorIndex < 0 || intent.ConceptPath == intent.AnchorPath)
            {
                error = "Move requires distinct, uniquely locatable target and anchor concepts.";
                return false;
            }

            bool adjacentSwap = intent.Kind == SpriteCardStructuralEditKind.MoveBefore
                ? anchorIndex + 1 == targetIndex
                : targetIndex + 1 == anchorIndex;
            if (!adjacentSwap)
            {
                error = "M17 reorder is bounded to one adjacent segment swap.";
                return false;
            }

            SourceItem moved = items[targetIndex];
            items.RemoveAt(targetIndex);
            anchorIndex = items.FindIndex(item => item.LocalId == anchorLocal);
            int insertionIndex = intent.Kind == SpriteCardStructuralEditKind.MoveBefore
                ? anchorIndex
                : anchorIndex + 1;
            items.Insert(insertionIndex, moved);
        }

        string rewritten = string.Concat(items.Select(item => item.RawText)) + program.Tail;
        candidate = source[..program.ContentStart] + rewritten + source[program.ContentEnd..];
        return true;
    }

    private static bool TryCreateSegmentExpression(
        SpriteCardNewSegment segment,
        string regionExpression,
        out string expression,
        out string error)
    {
        expression = string.Empty;
        error = string.Empty;
        if (segment.MinimumLength < 0 || segment.Weight < 0)
        {
            error = "Segment minimum length and weight must be non-negative.";
            return false;
        }

        if (segment.Sampling is not ("stretch" or "tile" or "crop"))
        {
            error = $"Sampling policy '{segment.Sampling}' is invalid.";
            return false;
        }

        string id = $"prefix + \".{segment.LocalId}\"";
        if (segment.Allocation == SpriteCardSegmentAllocation.Fixed)
        {
            expression = $"fixed({id}, {regionExpression}, {segment.MinimumLength})";
            return true;
        }

        expression = $"flex({id}, {regionExpression}, {segment.MinimumLength}, {segment.Weight}, \"{segment.Sampling}\")";
        return true;
    }

    private static bool TryResolveRegionExpression(
        string source,
        ObjectAssetDocument document,
        EdgeTarget edge,
        IReadOnlyList<SourceItem> items,
        string regionId,
        out string expression)
    {
        expression = string.Empty;
        if (!document.Regions.Any(region => region.Id == regionId))
        {
            return false;
        }

        ObjectAssetEdgeSegment? semantic = edge.Segments.Segments.FirstOrDefault(segment => segment.RegionId == regionId);
        if (semantic is null)
        {
            return false;
        }

        string semanticLocalId = semantic.Id.Split('.')[^1];
        SourceItem? item = items.SingleOrDefault(candidate => candidate.LocalId == semanticLocalId);
        if (item is null)
        {
            return false;
        }

        int open = source.IndexOf('(', item.ExpressionStart, item.ExpressionEnd - item.ExpressionStart);
        if (open < 0)
        {
            return false;
        }

        IReadOnlyList<SourceArgument> arguments = SplitArguments(source, open);
        if (arguments.Count < 2)
        {
            return false;
        }

        SourceArgument region = arguments[1];
        expression = source.Substring(region.Start, region.Length);
        return true;
    }

    private static bool ValidateSemanticResult(
        ObjectAssetDocument before,
        ObjectAssetDocument after,
        EdgeTarget edge,
        SpriteCardStructuralEditIntent intent,
        GraphicalConceptPath? targetPath,
        out string error)
    {
        error = string.Empty;
        IReadOnlyList<GraphicalConceptPath> beforePaths = PanelPaths(before, edge.PanelId);
        IReadOnlyList<GraphicalConceptPath> afterPaths = PanelPaths(after, edge.PanelId);
        if (afterPaths.Distinct().Count() != afterPaths.Count)
        {
            error = "The candidate creates duplicate Concept Paths.";
            return false;
        }

        ObjectAssetEdge beforeEdge = SelectEdge(before.Panels.Single(panel => panel.Id == edge.PanelId), edge.EdgeName);
        ObjectAssetEdge afterEdge = SelectEdge(after.Panels.Single(panel => panel.Id == edge.PanelId), edge.EdgeName);
        if (afterEdge.Segments.Count < 3 ||
            beforeEdge.Segments[0].Id != afterEdge.Segments[0].Id ||
            beforeEdge.Segments[^1].Id != afterEdge.Segments[^1].Id)
        {
            error = "The candidate must retain both edge caps and at least three segments.";
            return false;
        }

        if (intent.Kind is SpriteCardStructuralEditKind.InsertBefore or SpriteCardStructuralEditKind.InsertAfter)
        {
            if (targetPath is null || !afterPaths.Contains(targetPath.Value))
            {
                error = $"Inserted concept '{targetPath}' did not appear after compilation.";
                return false;
            }
        }
        else if (intent.Kind == SpriteCardStructuralEditKind.Remove)
        {
            if (targetPath is null || afterPaths.Contains(targetPath.Value))
            {
                error = $"Removed concept '{targetPath}' still exists after compilation.";
                return false;
            }
        }
        else
        {
            if (targetPath is null || intent.AnchorPath is null ||
                !afterPaths.Contains(targetPath.Value) || !afterPaths.Contains(intent.AnchorPath.Value))
            {
                error = "A move must preserve both target and anchor identities.";
                return false;
            }

            IReadOnlyList<GraphicalConceptPath> order = afterEdge.Segments
                .Select(segment => SegmentPath(edge.PanelId, segment.Id))
                .ToArray();
            int targetIndex = Array.FindIndex(order.ToArray(), path => path == targetPath.Value);
            int anchorIndex = Array.FindIndex(order.ToArray(), path => path == intent.AnchorPath.Value);
            bool ordered = intent.Kind == SpriteCardStructuralEditKind.MoveBefore
                ? targetIndex + 1 == anchorIndex
                : anchorIndex + 1 == targetIndex;
            if (!ordered)
            {
                error = "The compiled edge order does not match the requested adjacent move.";
                return false;
            }
        }

        return true;
    }

    private static GraphicalConceptPath? ResolveTargetPath(SpriteCardStructuralEditIntent intent)
    {
        if (intent.Kind is SpriteCardStructuralEditKind.InsertBefore or SpriteCardStructuralEditKind.InsertAfter)
        {
            if (intent.NewConcept is not null && GraphicalConceptPath.TryCreate(
                    intent.ParentPath.Value + "." + intent.NewConcept.LocalId,
                    out GraphicalConceptPath path))
            {
                return path;
            }

            return null;
        }

        return intent.ConceptPath;
    }

    private static GraphicalSourceLocation LocateStructuralSegment(
        string path,
        string source,
        string edgeName,
        ObjectAssetEdgeSegment segment)
    {
        string construct = edgeName is "top" or "bottom" ? "horizontalEdge" : "verticalEdge";
        if (TryFindOrderedProgram(source, construct, out OrderedProgram program, out _))
        {
            string localId = segment.Id.Split('.')[^1];
            SourceItem? item = program.Items.SingleOrDefault(candidate => candidate.LocalId == localId);
            if (item is not null)
            {
                return Location(path, source, item.ExpressionStart, item.ExpressionEnd - item.ExpressionStart);
            }
        }

        return LocateText(path, source, Quote(segment.RegionId));
    }

    private static bool TryResolveEdge(
        GraphicalConceptPath parentPath,
        string panelId,
        ObjectAssetDocument document,
        out EdgeTarget edge,
        out string error)
    {
        edge = default;
        error = string.Empty;
        string[] parts = parentPath.Value.Split('.');
        if (parts.Length != 3 || parts[0] != "panel" || parts[1] != panelId ||
            parts[2] is not ("top" or "right" or "bottom" or "left"))
        {
            error = $"Parent '{parentPath}' is not an ordered edge of panel '{panelId}'.";
            return false;
        }

        ObjectAssetPanel panel = document.Panels.Single(candidate => candidate.Id == panelId);
        edge = new EdgeTarget(panelId, parts[2], SelectEdge(panel, parts[2]));
        return true;
    }

    private static bool TryResolveExpectedConstruct(
        GraphicalConceptPath parentPath,
        string panelId,
        out string construct,
        out string error)
    {
        construct = string.Empty;
        error = string.Empty;
        string[] parts = parentPath.Value.Split('.');
        if (parts.Length != 3 || parts[0] != "panel" || parts[1] != panelId ||
            parts[2] is not ("top" or "right" or "bottom" or "left"))
        {
            error = $"Parent '{parentPath}' is not an ordered edge of panel '{panelId}'.";
            return false;
        }

        construct = parts[2] is "top" or "bottom" ? "horizontalEdge" : "verticalEdge";
        return true;
    }

    private static ObjectAssetEdge SelectEdge(ObjectAssetPanel panel, string edgeName)
    {
        return edgeName switch
        {
            "top" => panel.Top,
            "right" => panel.Right,
            "bottom" => panel.Bottom,
            "left" => panel.Left,
            _ => throw new ArgumentOutOfRangeException(nameof(edgeName)),
        };
    }

    private static IReadOnlyList<GraphicalConceptPath> PanelPaths(ObjectAssetDocument document, string panelId)
    {
        ObjectAssetPanel panel = document.Panels.Single(candidate => candidate.Id == panelId);
        return new[] { panel.Top, panel.Right, panel.Bottom, panel.Left }
            .SelectMany(edge => edge.Segments)
            .Select(segment => SegmentPath(panelId, segment.Id))
            .ToArray();
    }

    private static bool TryFindOrderedProgram(
        string source,
        string functionName,
        out OrderedProgram program,
        out string error)
    {
        program = default!;
        error = string.Empty;
        MatchCollection functionMatches = Regex.Matches(
            MaskCommentsAndStrings(source),
            $@"\bfunction\s+{Regex.Escape(functionName)}\s*\(");
        if (functionMatches.Count != 1)
        {
            error = $"Expected one authored '{functionName}' construct, found {functionMatches.Count}.";
            return false;
        }

        int bodyOpen = FindNext(source, '{', functionMatches[0].Index + functionMatches[0].Length);
        int bodyClose = MatchDelimiter(source, bodyOpen, '{', '}');
        if (bodyOpen < 0 || bodyClose < 0)
        {
            error = $"The authored '{functionName}' body is incomplete.";
            return false;
        }

        string bodyMask = MaskCommentsAndStrings(source[bodyOpen..(bodyClose + 1)]);
        MatchCollection segmentMatches = Regex.Matches(bodyMask, @"\bsegments\s*:\s*\[");
        if (segmentMatches.Count != 1)
        {
            error = $"Expected one ordered segments list in '{functionName}', found {segmentMatches.Count}.";
            return false;
        }

        int openBracket = bodyOpen + segmentMatches[0].Index + segmentMatches[0].Value.LastIndexOf('[');
        int closeBracket = MatchDelimiter(source, openBracket, '[', ']');
        if (closeBracket < 0 || closeBracket > bodyClose)
        {
            error = $"The ordered segments list in '{functionName}' is incomplete.";
            return false;
        }

        IReadOnlyList<SourceItem> items = SplitArrayItems(source, openBracket + 1, closeBracket, out string tail);
        if (items.Count == 0 || items.Any(item => item.LocalId is null))
        {
            error = $"The '{functionName}' list contains an unsupported or unidentifiable segment expression.";
            return false;
        }

        program = new OrderedProgram(openBracket, closeBracket, openBracket + 1, closeBracket, items, tail);
        return true;
    }

    private static IReadOnlyList<SourceItem> SplitArrayItems(
        string source,
        int contentStart,
        int contentEnd,
        out string tail)
    {
        var items = new List<SourceItem>();
        int rawStart = contentStart;
        int parentheses = 0;
        int braces = 0;
        bool inString = false;
        bool escaped = false;
        bool lineComment = false;
        bool blockComment = false;
        for (int index = contentStart; index < contentEnd; index++)
        {
            char character = source[index];
            char next = index + 1 < contentEnd ? source[index + 1] : '\0';
            if (lineComment)
            {
                if (character == '\n')
                {
                    lineComment = false;
                }

                continue;
            }

            if (blockComment)
            {
                if (character == '*' && next == '/')
                {
                    blockComment = false;
                    index++;
                }

                continue;
            }

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

            if (character == '/' && next == '/')
            {
                lineComment = true;
                index++;
            }
            else if (character == '/' && next == '*')
            {
                blockComment = true;
                index++;
            }
            else if (character == '"')
            {
                inString = true;
            }
            else if (character == '(')
            {
                parentheses++;
            }
            else if (character == ')')
            {
                parentheses--;
            }
            else if (character == '{')
            {
                braces++;
            }
            else if (character == '}')
            {
                braces--;
            }
            else if (character == ',' && parentheses == 0 && braces == 0)
            {
                int rawEnd = index + 1;
                int expressionStart = SkipTrivia(source, rawStart, rawEnd);
                int expressionEnd = index;
                while (expressionEnd > expressionStart && char.IsWhiteSpace(source[expressionEnd - 1]))
                {
                    expressionEnd--;
                }

                string raw = source.Substring(rawStart, rawEnd - rawStart);
                items.Add(new SourceItem(
                    rawStart,
                    rawEnd,
                    expressionStart,
                    expressionEnd,
                    ExtractLocalId(source.Substring(expressionStart, expressionEnd - expressionStart)),
                    raw));
                rawStart = rawEnd;
            }
        }

        tail = source.Substring(rawStart, contentEnd - rawStart);
        return items;
    }

    private static string? ExtractLocalId(string expression)
    {
        Match prefixed = Regex.Match(expression, @"prefix\s*\+\s*\""\.(?<id>[A-Za-z][A-Za-z0-9_-]*)\""");
        if (prefixed.Success)
        {
            return prefixed.Groups["id"].Value;
        }

        Match literal = Regex.Match(expression, @"^[^(]+\(\s*\""[^\""\r\n]*\.(?<id>[A-Za-z][A-Za-z0-9_-]*)\""");
        return literal.Success ? literal.Groups["id"].Value : null;
    }

    private static int UniqueIndex(IReadOnlyList<SourceItem> items, string localId, out bool ambiguous)
    {
        int[] matches = items.Select((item, index) => (item, index))
            .Where(pair => pair.item.LocalId == localId)
            .Select(pair => pair.index)
            .ToArray();
        ambiguous = matches.Length > 1;
        return matches.Length == 1 ? matches[0] : -1;
    }

    private static int SkipTrivia(string source, int start, int end)
    {
        int index = start;
        while (index < end)
        {
            if (char.IsWhiteSpace(source[index]))
            {
                index++;
            }
            else if (index + 1 < end && source[index] == '/' && source[index + 1] == '/')
            {
                index += 2;
                while (index < end && source[index] != '\n')
                {
                    index++;
                }
            }
            else if (index + 1 < end && source[index] == '/' && source[index + 1] == '*')
            {
                int close = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = close < 0 || close >= end ? end : close + 2;
            }
            else
            {
                break;
            }
        }

        return index;
    }

    private static int FindNext(string source, char target, int start)
    {
        string mask = MaskCommentsAndStrings(source);
        return mask.IndexOf(target, start);
    }

    private static int MatchDelimiter(string source, int openIndex, char open, char close)
    {
        if (openIndex < 0)
        {
            return -1;
        }

        string mask = MaskCommentsAndStrings(source);
        int depth = 0;
        for (int index = openIndex; index < mask.Length; index++)
        {
            if (mask[index] == open)
            {
                depth++;
            }
            else if (mask[index] == close && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static string MaskCommentsAndStrings(string source)
    {
        char[] mask = source.ToCharArray();
        bool inString = false;
        bool escaped = false;
        bool lineComment = false;
        bool blockComment = false;
        for (int index = 0; index < mask.Length; index++)
        {
            char character = source[index];
            char next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (lineComment)
            {
                if (character == '\n')
                {
                    lineComment = false;
                }
                else
                {
                    mask[index] = ' ';
                }

                continue;
            }

            if (blockComment)
            {
                mask[index] = character is '\r' or '\n' ? character : ' ';
                if (character == '*' && next == '/')
                {
                    mask[index + 1] = ' ';
                    blockComment = false;
                    index++;
                }

                continue;
            }

            if (inString)
            {
                mask[index] = ' ';
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

            if (character == '/' && next == '/')
            {
                mask[index] = ' ';
                mask[index + 1] = ' ';
                lineComment = true;
                index++;
            }
            else if (character == '/' && next == '*')
            {
                mask[index] = ' ';
                mask[index + 1] = ' ';
                blockComment = true;
                index++;
            }
            else if (character == '"')
            {
                mask[index] = ' ';
                inString = true;
            }
        }

        return new string(mask);
    }

    private static string LineIndentation(string source, int index)
    {
        int lineStart = source.LastIndexOf('\n', Math.Max(0, index - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        int cursor = lineStart;
        while (cursor < index && source[cursor] is ' ' or '\t')
        {
            cursor++;
        }

        return source.Substring(lineStart, cursor - lineStart);
    }

    private static string LocalName(GraphicalConceptPath path)
    {
        return path.Value.Split('.')[^1];
    }

    private readonly record struct EdgeTarget(string PanelId, string EdgeName, ObjectAssetEdge Segments)
    {
        public bool IsHorizontal => EdgeName is "top" or "bottom";
    }

    private sealed record OrderedProgram(
        int OpenBracket,
        int CloseBracket,
        int ContentStart,
        int ContentEnd,
        IReadOnlyList<SourceItem> Items,
        string Tail);

    private sealed record SourceItem(
        int RawStart,
        int RawEnd,
        int ExpressionStart,
        int ExpressionEnd,
        string? LocalId,
        string RawText);
}
