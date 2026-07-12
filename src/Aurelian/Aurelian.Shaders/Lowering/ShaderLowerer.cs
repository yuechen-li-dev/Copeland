using System.Text;
using Aurelian.Shaders.Ast;
using Aurelian.Shaders.Diagnostics;

namespace Aurelian.Shaders.Lowering;

public sealed record StreamBinding(string Type, string Name, string Semantic, int Line, int Column);
public sealed record StreamLayout(IReadOnlyList<StreamBinding> Bindings, IReadOnlyList<Diagnostic> Diagnostics);
public sealed record LoweringResult(string Hlsl, IReadOnlyList<Diagnostic> Diagnostics, StreamUsageAnalysisResult StreamUsage, StageIoLayout StageIo);
public sealed record ShaderSpecialization(IReadOnlyDictionary<string, bool> BoolValues);
public enum StreamSemanticKind { VertexInput, VertexOutput, PixelInput, PixelOutput, Interpolant, Unknown }
public sealed record StageIoLayout(
    IReadOnlyList<SdslStream> VSInput,
    IReadOnlyList<SdslStream> VSOutput,
    IReadOnlyList<SdslStream> PSInput,
    IReadOnlyList<SdslStream> PSOutput);

public sealed class ShaderLowerer
{
    public string EmitHlsl(HlslDocument document) => document.Source;

    public LoweringResult LowerSdslToHlsl(SdslShader shader, ShaderSpecialization? specialization = null) => LowerSdslDocumentToHlsl(new SdslDocument([shader], [], []), shader.Name, specialization);

    public LoweringResult LowerSdslDocumentToHlsl(SdslDocument doc, string entryShaderName, ShaderSpecialization? specialization = null)
    {
        var diags = new List<Diagnostic>(doc.Diagnostics);
        var registry = new Dictionary<string, SdslShader>(StringComparer.Ordinal);
        foreach (var s in doc.Shaders)
        {
            if (!registry.TryAdd(s.Name, s)) diags.Add(Diagnostic.Create("SD316", $"Duplicate shader name '{s.Name}'."));
        }
        if (!registry.TryGetValue(entryShaderName, out var shader)) return new("", diags, new StreamUsageAnalysisResult([], [], []), new StageIoLayout([], [], [], []));

        SdslShader? baseShader = null;
        if (shader.BaseShaders.Count > 1) diags.Add(Diagnostic.Create("SD313", $"Multiple base shaders are unsupported for '{shader.Name}'."));
        else if (shader.BaseShaders.Count == 1)
        {
            var baseNameRaw = shader.BaseShaders[0];
            if (baseNameRaw.Contains('<')) diags.Add(Diagnostic.Create("SD314", $"Generic base specialization '{baseNameRaw}' is unsupported."));
            else if (!registry.TryGetValue(baseNameRaw, out baseShader)) diags.Add(Diagnostic.Create("SD310", $"Unresolved base shader '{baseNameRaw}'."));
            else if (baseShader.Name == shader.Name || baseShader.BaseShaders.Contains(shader.Name, StringComparer.Ordinal)) diags.Add(Diagnostic.Create("SD311", $"Inheritance cycle detected for '{shader.Name}'."));
        }

        var mergedStreams = MergeStreams(baseShader, shader, diags);
        var genericSubstitutions = BuildGenericSubstitutions(shader, specialization, diags);
        var methods = MergeMethods(baseShader, shader, diags);
        var stageMethods = methods.Values.Where(m => m.Name is "VSMain" or "PSMain" or "Shading").ToArray();
        var usage = new StreamUsageAnalyzer().Analyze(stageMethods, mergedStreams.Select(s => s.Name).ToArray());
        diags.AddRange(usage.Diagnostics);

        var sb = new StringBuilder();
        sb.AppendLine($"// Lowered from {shader.Name}");
        var io = BuildStageIoLayout(mergedStreams, usage.Usage, diags);
        if (io.VSInput.Count > 0) EmitStageStruct(sb, "StriVVSInput", io.VSInput);
        EmitStageStruct(sb, "StriVVSOutput", io.VSOutput);
        EmitStageStruct(sb, "StriVPSInput", io.PSInput);
        EmitStageStruct(sb, "StriVPSOutput", io.PSOutput);

        var neededHelpers = methods.Values.SelectMany(m => m.BaseCalls).Select(c => c.MethodName).ToHashSet(StringComparer.Ordinal);
        if (baseShader is not null)
        {
            foreach (var bm in baseShader.Methods.Where(m => neededHelpers.Contains(m.Name)))
            {
                var helperStreamType = bm.Name == "VSMain" ? "StriVVSOutput" : bm.Name == "PSMain" ? "StriVPSInput" : "StriVPSInput";
                sb.AppendLine($"void __base_{baseShader.Name}_{bm.Name}(inout {helperStreamType} streams)");
                sb.AppendLine("{");
                foreach (var line in bm.Body.Split('\n')) sb.AppendLine($"    {line.TrimEnd()}");
                sb.AppendLine("}"); sb.AppendLine();
            }
        }

        foreach (var method in methods.Values)
        {
            var rewritten = RewriteBaseCalls(method, baseShader, methods, diags);
            rewritten = SubstituteIdentifiers(rewritten, genericSubstitutions);
            if (method.Name == "VSMain")
            {
                var hasInput = io.VSInput.Count > 0;
                if (hasInput) rewritten = RewriteInputOnlyStreamsForVsBody(rewritten, io.VSInput, io.VSOutput);
                sb.AppendLine(hasInput ? "StriVVSOutput VSMain(StriVVSInput input)" : "StriVVSOutput VSMain()");
                sb.AppendLine("{");
                sb.AppendLine("    StriVVSOutput streams;");
                if (hasInput)
                {
                    var passThroughNames = io.VSInput.Select(s => s.Name).Intersect(io.VSOutput.Select(s => s.Name), StringComparer.Ordinal);
                    foreach (var n in passThroughNames) sb.AppendLine($"    streams.{n} = input.{n};");
                }
                foreach (var l in rewritten.Split('\n')) sb.AppendLine($"    {l.TrimEnd()}");
                sb.AppendLine("    return streams;");
                sb.AppendLine("}");
            }
            else if (method.Name == "PSMain") { var suffix = method.ReturnType == "float4" ? " : SV_Target" : string.Empty; sb.AppendLine($"{method.ReturnType} PSMain(StriVPSInput streams){suffix}"); sb.AppendLine("{"); foreach (var l in rewritten.Split('\n')) sb.AppendLine($"    {l.TrimEnd()}"); sb.AppendLine("}"); }
        }

        return new(sb.ToString(), diags, usage, io);
    }

    private static IReadOnlyDictionary<string, string> BuildGenericSubstitutions(SdslShader shader, ShaderSpecialization? specialization, List<Diagnostic> diags)
    {
        var values = specialization?.BoolValues ?? new Dictionary<string, bool>(StringComparer.Ordinal);
        var supported = new Dictionary<string, string>(StringComparer.Ordinal);
        var known = shader.GenericParameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var pair in values)
        {
            if (!known.Contains(pair.Key))
                diags.Add(Diagnostic.Create("SD322", $"Specialization provided for unknown generic parameter '{pair.Key}'."));
        }

        foreach (var p in shader.GenericParameters)
        {
            if (!string.Equals(p.TypeText, "bool", StringComparison.Ordinal))
            {
                diags.Add(Diagnostic.Create("SD321", $"Unsupported generic parameter type '{p.TypeText}' for '{p.Name}'.", p.Span.Line, p.Span.Column));
                continue;
            }

            if (!values.TryGetValue(p.Name, out var v))
            {
                diags.Add(Diagnostic.Create("SD320", $"Missing specialization value for generic parameter '{p.Name}'.", p.Span.Line, p.Span.Column));
                continue;
            }

            supported[p.Name] = v ? "true" : "false";
        }

        return supported;
    }

    public static string SubstituteIdentifiers(string text, IReadOnlyDictionary<string, string> substitutions)
    {
        if (substitutions.Count == 0 || string.IsNullOrEmpty(text)) return text;
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '"')
            {
                var start = i++;
                while (i < text.Length)
                {
                    if (text[i] == '\\') { i += 2; continue; }
                    if (text[i] == '"') { i++; break; }
                    i++;
                }
                sb.Append(text.AsSpan(start, i - start));
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                var start = i;
                i += 2;
                while (i < text.Length && text[i] != '\n') i++;
                sb.Append(text.AsSpan(start, i - start));
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var start = i; i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                if (i + 1 < text.Length) i += 2;
                sb.Append(text.AsSpan(start, i - start));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                var ident = text[start..i];
                if (substitutions.TryGetValue(ident, out var repl)) sb.Append(repl);
                else sb.Append(ident);
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    private static string RewriteBaseCalls(SdslStageMethod method, SdslShader? baseShader, Dictionary<string,SdslStageMethod> methods, List<Diagnostic> diags)
    {
        var body = method.Body;
        foreach (var bc in method.BaseCalls.OrderByDescending(x => x.Span.Start))
        {
            if (baseShader is null || !baseShader.Methods.Any(m => m.Name == bc.MethodName))
            {
                diags.Add(Diagnostic.Create("SD312", $"Base method '{bc.MethodName}' not found for '{method.Name}'.", bc.Span.Line, bc.Span.Column));
                continue;
            }
            var replacement = bc.ArgumentCount == 0 ? $"__base_{baseShader.Name}_{bc.MethodName}(streams);" : $"__base_{baseShader.Name}_{bc.MethodName}(streams, {bc.ArgumentText});";
            body = body.Remove(bc.Span.Start, bc.Span.Length).Insert(bc.Span.Start, replacement);
        }
        return body;
    }

    private static Dictionary<string,SdslStageMethod> MergeMethods(SdslShader? baseShader, SdslShader child, List<Diagnostic> diags)
    {
        var map = new Dictionary<string,SdslStageMethod>(StringComparer.Ordinal);
        if (baseShader is not null) foreach (var bm in baseShader.Methods) map[bm.Name] = bm;
        foreach (var cm in child.Methods)
        {
            var hasOverride = cm.Modifiers.Contains("override", StringComparer.Ordinal);
            if (map.ContainsKey(cm.Name) && !hasOverride) diags.Add(Diagnostic.Create("SD313", $"Method '{cm.Name}' must be declared override."));
            if (!map.ContainsKey(cm.Name) && hasOverride && baseShader is not null) diags.Add(Diagnostic.Create("SD312", $"Override method '{cm.Name}' has no base implementation."));
            map[cm.Name] = cm;
        }
        return map;
    }

    private static List<SdslStream> MergeStreams(SdslShader? baseShader, SdslShader child, List<Diagnostic> diags)
    {
        var outList = new List<SdslStream>();
        foreach (var s in baseShader?.Streams ?? []) outList.Add(s);
        foreach (var s in child.Streams)
        {
            var existing = outList.FirstOrDefault(x => x.Name == s.Name);
            if (existing is null)
            {
                if (outList.Any(x => x.Semantic.Equals(s.Semantic, StringComparison.OrdinalIgnoreCase))) diags.Add(Diagnostic.Create("SD315", $"Conflicting stream semantic '{s.Semantic}'.", s.Span.Line, s.Span.Column));
                outList.Add(s);
                continue;
            }
            if (existing.Type != s.Type || existing.Semantic != s.Semantic) diags.Add(Diagnostic.Create("SD315", $"Conflicting stream '{s.Name}'.", s.Span.Line, s.Span.Column));
        }
        return outList;
    }

    private static void EmitStageStruct(StringBuilder sb, string typeName, IReadOnlyList<SdslStream> streams)
    {
        sb.AppendLine($"struct {typeName}");
        sb.AppendLine("{");
        foreach (var stream in streams) sb.AppendLine($"    {stream.Type} {stream.Name} : {stream.Semantic};");
        sb.AppendLine("};");
        sb.AppendLine();
    }

    private static StageIoLayout BuildStageIoLayout(IReadOnlyList<SdslStream> mergedStreams, IReadOnlyList<StreamUsage> usage, List<Diagnostic> diags)
    {
        var usageByName = usage.ToDictionary(u => u.StreamName, StringComparer.Ordinal);
        var vsIn = new List<SdslStream>();
        var vsOut = new List<SdslStream>();
        var psIn = new List<SdslStream>();
        var psOut = new List<SdslStream>();
        foreach (var s in mergedStreams)
        {
            var kind = ClassifySemantic(s.Semantic);
            if (kind is StreamSemanticKind.Unknown)
                diags.Add(Diagnostic.Create("SD330", $"Unknown stream semantic '{s.Semantic}' classified as interpolant.", s.Span.Line, s.Span.Column));
            if (kind is StreamSemanticKind.VertexInput or StreamSemanticKind.Interpolant)
                vsIn.Add(s);
            if (kind is StreamSemanticKind.PixelOutput)
            {
                diags.Add(Diagnostic.Create("SD331", $"Pixel-output semantic '{s.Semantic}' is excluded from vertex output.", s.Span.Line, s.Span.Column));
                psOut.Add(s);
                continue;
            }

            if (kind is StreamSemanticKind.VertexInput)
                continue;

            vsOut.Add(s);
            if (ShouldIncludeInPsInput(s, kind, usageByName, diags))
                psIn.Add(s);
        }
        return new(vsIn, vsOut, psIn, psOut);
    }

    private static bool ShouldIncludeInPsInput(SdslStream stream, StreamSemanticKind kind, IReadOnlyDictionary<string, StreamUsage> usageByName, IReadOnlyList<Diagnostic> diags)
    {
        if (kind is StreamSemanticKind.PixelOutput)
            return false;

        if (kind is StreamSemanticKind.Unknown)
            return true;

        if (string.Equals(stream.Semantic, "SV_Position", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!usageByName.TryGetValue(stream.Name, out var usage))
            return true;

        var hasUncertainAccess = diags.Any(d =>
            d.Code == "SD341" &&
            d.Message.Contains($"'{stream.Name}'", StringComparison.Ordinal));
        if (hasUncertainAccess)
            return true;

        if (usage.PSRead || usage.UnknownRead)
            return true;

        return false;
    }

    private static StreamSemanticKind ClassifySemantic(string semanticRaw)
    {
        var semantic = semanticRaw.Trim().ToUpperInvariant();
        if (semantic is "SV_POSITION") return StreamSemanticKind.VertexOutput;
        if (semantic.StartsWith("SV_TARGET", StringComparison.Ordinal)) return StreamSemanticKind.PixelOutput;
        if (semantic.StartsWith("BLENDINDICES", StringComparison.Ordinal) || semantic.StartsWith("BLENDWEIGHT", StringComparison.Ordinal)) return StreamSemanticKind.VertexInput;
        if (semantic.StartsWith("COLOR", StringComparison.Ordinal) ||
            semantic.StartsWith("TEXCOORD", StringComparison.Ordinal) ||
            semantic.StartsWith("NORMAL", StringComparison.Ordinal) ||
            semantic.StartsWith("TANGENT", StringComparison.Ordinal) ||
            semantic.StartsWith("BINORMAL", StringComparison.Ordinal)) return StreamSemanticKind.Interpolant;
        if (semantic == "POSITION") return StreamSemanticKind.VertexInput;
        return StreamSemanticKind.Unknown;
    }

    private static string RewriteInputOnlyStreamsForVsBody(string text, IReadOnlyList<SdslStream> vsInput, IReadOnlyList<SdslStream> vsOutput)
    {
        var inputOnly = vsInput.Select(s => s.Name).Except(vsOutput.Select(s => s.Name), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (inputOnly.Count == 0 || string.IsNullOrEmpty(text)) return text;
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '"')
            {
                var start = i++;
                while (i < text.Length)
                {
                    if (text[i] == '\\') { i += 2; continue; }
                    if (text[i] == '"') { i++; break; }
                    i++;
                }
                sb.Append(text.AsSpan(start, i - start));
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                var start = i; i += 2; while (i < text.Length && text[i] != '\n') i++; sb.Append(text.AsSpan(start, i - start)); continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var start = i; i += 2; while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++; if (i + 1 < text.Length) i += 2; sb.Append(text.AsSpan(start, i - start)); continue;
            }

            if (i + 8 <= text.Length && text.AsSpan(i, 8).SequenceEqual("streams."))
            {
                var nameStart = i + 8;
                var j = nameStart;
                if (j < text.Length && (char.IsLetter(text[j]) || text[j] == '_'))
                {
                    j++;
                    while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
                    var ident = text[nameStart..j];
                    if (inputOnly.Contains(ident)) { sb.Append("input."); sb.Append(ident); i = j; continue; }
                }
            }
            sb.Append(c); i++;
        }
        return sb.ToString();
    }
}
