using Aurelian.Shaders.Ast;
using Aurelian.Shaders.Diagnostics;
using Aurelian.Shaders.Lexing;

namespace Aurelian.Shaders.Lowering;

public enum StreamAccessKind { Read, Write, ReadWrite, Unknown }
public enum StageKind { Vertex, Pixel, Unknown }

public sealed record StreamAccess(string StreamName, StageKind Stage, StreamAccessKind Kind, SourceSpan Span);
public sealed record StreamUsage(string StreamName, bool VSRead, bool VSWrite, bool PSRead, bool PSWrite, bool UnknownRead, bool UnknownWrite);
public sealed record StreamUsageAnalysisResult(IReadOnlyList<StreamAccess> Accesses, IReadOnlyList<StreamUsage> Usage, IReadOnlyList<Diagnostic> Diagnostics);

public sealed class StreamUsageAnalyzer
{
    public StreamUsageAnalysisResult Analyze(IReadOnlyList<SdslStageMethod> methods, IReadOnlyCollection<string>? declaredStreams = null)
    {
        var accesses = new List<StreamAccess>();
        var diags = new List<Diagnostic>();
        var declared = declaredStreams is null ? null : new HashSet<string>(declaredStreams, StringComparer.Ordinal);
        foreach (var method in methods)
        {
            var stage = ClassifyStage(method.Name);
            AnalyzeMethodBody(method.Body, method.Span, stage, accesses, diags, declared);
        }

        var usage = accesses
            .GroupBy(a => a.StreamName, StringComparer.Ordinal)
            .Select(g => new StreamUsage(
                g.Key,
                VSRead: g.Any(a => a.Stage == StageKind.Vertex && (a.Kind == StreamAccessKind.Read || a.Kind == StreamAccessKind.ReadWrite || a.Kind == StreamAccessKind.Unknown)),
                VSWrite: g.Any(a => a.Stage == StageKind.Vertex && (a.Kind == StreamAccessKind.Write || a.Kind == StreamAccessKind.ReadWrite || a.Kind == StreamAccessKind.Unknown)),
                PSRead: g.Any(a => a.Stage == StageKind.Pixel && (a.Kind == StreamAccessKind.Read || a.Kind == StreamAccessKind.ReadWrite || a.Kind == StreamAccessKind.Unknown)),
                PSWrite: g.Any(a => a.Stage == StageKind.Pixel && (a.Kind == StreamAccessKind.Write || a.Kind == StreamAccessKind.ReadWrite || a.Kind == StreamAccessKind.Unknown)),
                UnknownRead: g.Any(a => a.Stage == StageKind.Unknown && (a.Kind == StreamAccessKind.Read || a.Kind == StreamAccessKind.ReadWrite || a.Kind == StreamAccessKind.Unknown)),
                UnknownWrite: g.Any(a => a.Stage == StageKind.Unknown && (a.Kind == StreamAccessKind.Write || a.Kind == StreamAccessKind.ReadWrite || a.Kind == StreamAccessKind.Unknown))))
            .OrderBy(x => x.StreamName, StringComparer.Ordinal)
            .ToArray();

        return new(accesses, usage, diags);
    }

    private static StageKind ClassifyStage(string methodName)
        => methodName == "VSMain" ? StageKind.Vertex : methodName == "PSMain" || methodName == "Shading" ? StageKind.Pixel : StageKind.Unknown;

    private static void AnalyzeMethodBody(string body, SourceSpan methodSpan, StageKind stage, List<StreamAccess> accesses, List<Diagnostic> diags, HashSet<string>? declared)
    {
        for (var i = 0; i < body.Length; i++)
        {
            if (TrySkipTrivia(body, ref i)) continue;
            if (!StartsWithStreams(body, i)) continue;

            var streamTokenStart = i;
            i += 8;
            if (i >= body.Length || !(char.IsLetter(body[i]) || body[i] == '_')) continue;
            var nameStart = i;
            i++;
            while (i < body.Length && (char.IsLetterOrDigit(body[i]) || body[i] == '_')) i++;
            var streamName = body[nameStart..i];
            var end = SkipMemberChain(body, i);
            var kind = ClassifyAccess(body, streamTokenStart, end, diags, methodSpan, streamName);

            accesses.Add(new(streamName, stage, kind, new SourceSpan(streamTokenStart, end - streamTokenStart, methodSpan.Line, methodSpan.Column)));
            if (declared is not null && !declared.Contains(streamName)) diags.Add(Diagnostic.Create("SD340", $"Reference to undeclared stream '{streamName}'.", methodSpan.Line, methodSpan.Column));
            i = end - 1;
        }
    }

    private static StreamAccessKind ClassifyAccess(string body, int start, int end, List<Diagnostic> diags, SourceSpan methodSpan, string streamName)
    {
        var left = SkipBackwardWs(body, start - 1);
        if (left >= 1)
        {
            var op2 = body.Substring(left - 1, 2);
            if (op2 is "++" or "--") return StreamAccessKind.ReadWrite;
        }

        var right = SkipWs(body, end);
        if (right + 1 < body.Length)
        {
            var op2 = body.Substring(right, 2);
            if (op2 is "++" or "--") return StreamAccessKind.ReadWrite;
            if (op2 is "+=" or "-=" or "*=" or "/=" or "%=" or "&=" or "|=" or "^=") return StreamAccessKind.ReadWrite;
            if (op2 is "==" or "!=" or ">=" or "<=") return StreamAccessKind.Read;
        }

        if (right < body.Length && body[right] == '=') return StreamAccessKind.Write;
        if (right < body.Length && "?:,;)]+-*/%!<>&|^".Contains(body[right])) return StreamAccessKind.Read;

        diags.Add(Diagnostic.Create("SD341", $"Stream access classification uncertain for '{streamName}'. Treating as read.", methodSpan.Line, methodSpan.Column));
        return StreamAccessKind.Unknown;
    }

    private static int SkipMemberChain(string body, int i)
    {
        var p = i;
        while (p < body.Length)
        {
            p = SkipWs(body, p);
            if (p >= body.Length || body[p] != '.') break;
            p++;
            p = SkipWs(body, p);
            if (p >= body.Length || !(char.IsLetter(body[p]) || body[p] == '_')) break;
            p++;
            while (p < body.Length && (char.IsLetterOrDigit(body[p]) || body[p] == '_')) p++;
        }
        return p;
    }

    private static bool StartsWithStreams(string text, int i)
        => i + 8 <= text.Length && text.AsSpan(i, 8).SequenceEqual("streams.");

    private static bool TrySkipTrivia(string body, ref int i)
    {
        if (body[i] == '"')
        {
            i++;
            while (i < body.Length)
            {
                if (body[i] == '\\') { i += 2; continue; }
                if (body[i] == '"') break;
                i++;
            }
            return true;
        }
        if (body[i] == '/' && i + 1 < body.Length && body[i + 1] == '/')
        {
            i += 2;
            while (i < body.Length && body[i] != '\n') i++;
            return true;
        }
        if (body[i] == '/' && i + 1 < body.Length && body[i + 1] == '*')
        {
            i += 2;
            while (i + 1 < body.Length && !(body[i] == '*' && body[i + 1] == '/')) i++;
            if (i + 1 < body.Length) i++;
            return true;
        }
        return false;
    }

    private static int SkipWs(string body, int i) { while (i < body.Length && char.IsWhiteSpace(body[i])) i++; return i; }
    private static int SkipBackwardWs(string body, int i) { while (i >= 0 && char.IsWhiteSpace(body[i])) i--; return i; }
}
