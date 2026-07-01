using System.Text.RegularExpressions;
using Aurelian.Shaders.Ast;
using Aurelian.Shaders.Diagnostics;
using Aurelian.Shaders.Lexing;

namespace Aurelian.Shaders.Parsing;

public sealed class ShaderParser
{
    public ParseResult<HlslDocument> ParseHlsl(string source)
    {
        var diags = new List<Diagnostic>();
        var funcs = new List<HlslFunction>();
        var regex = new Regex(@"([A-Za-z_]\w[\w<>\s\*]*\s+[A-Za-z_]\w*\s*\([^\)]*\)\s*(?::\s*[A-Za-z_]\w*)?)\s*\{", RegexOptions.Multiline);
        foreach (Match m in regex.Matches(source))
        {
            var bodyStart = m.Index + m.Length - 1;
            var body = ReadBalancedBlock(source, bodyStart, out _);
            funcs.Add(new(m.Groups[1].Value.Trim(), body));
        }
        return new(new(source, funcs), diags);
    }

    public ParseResult<SdslShader> ParseSdsl(string source)
    {
        var doc = ParseSdslDocument(source);
        return doc.Document is null || doc.Document.Shaders.Count == 0
            ? new(null, doc.Diagnostics)
            : new(doc.Document.Shaders[0], doc.Diagnostics);
    }

    public ParseResult<SdslDocument> ParseSdslDocument(string source)
    {
        var diags = new List<Diagnostic>();
        var shaders = new List<SdslShader>();
        var effects = new List<SdslEffectBlock>();

        var shaderHeader = new Regex(@"shader\s+([A-Za-z_]\w*)(\s*<([^>]+)>)?(\s*:\s*([^\{\r\n]+))?\s*\{", RegexOptions.Multiline);
        var matches = shaderHeader.Matches(source);
        foreach (Match header in matches)
        {
            var name = header.Groups[1].Value;
            var genericParametersText = header.Groups[3].Success ? header.Groups[3].Value.Trim() : null;
            var baseShaders = header.Groups[5].Success
                ? header.Groups[5].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList()
                : new List<string>();

            var block = ReadBalancedBlock(source, header.Index + header.Length - 1, out _);
            var bodySource = block[1..^1];

            var genericParameters = ParseGenericParameters(source, header.Groups[3], genericParametersText, diags);

            var streams = new List<SdslStream>();
            var streamRegex = new Regex(@"stage\s+stream\s+([^\s]+)\s+([A-Za-z_]\w*)\s*:\s*([A-Za-z_]\w*)\s*;", RegexOptions.Multiline);
            foreach (Match m in streamRegex.Matches(bodySource))
            {
                var span = GetSpan(source, header.Index + m.Index);
                streams.Add(new(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, span));
            }

            var methods = new List<SdslStageMethod>();
            var methodRegex = new Regex(@"(stage\s+(override\s+)?)\s*([^\s]+)\s+([A-Za-z_]\w*)\s*\(([^\)]*)\)\s*\{", RegexOptions.Multiline);
            foreach (Match m in methodRegex.Matches(bodySource))
            {
                var body = ReadBalancedBlock(bodySource, m.Index + m.Length - 1, out _);
                var span = GetSpan(source, header.Index + m.Index);
                var bodyText = body[1..^1].Trim();
                var baseCalls = BaseCallScanner.Scan(bodyText, new SourceSpan(0, 0, span.Line, span.Column));
                var mods = m.Groups[2].Success ? new[] { "stage", "override" } : new[] { "stage" };
                methods.Add(new(m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value, bodyText, baseCalls, mods, span));
            }

            shaders.Add(new(name, genericParametersText, genericParameters, baseShaders, streams, methods, new[] { "shader" }));
        }

        ParseEffectBlocks(source, effects, diags);

        if (shaders.Count == 0)
            diags.Add(Diagnostic.Create("SD000", "Missing shader header"));

        return new(new SdslDocument(shaders, effects, diags), diags);
    }

    private static void ParseEffectBlocks(string source, List<SdslEffectBlock> effects, List<Diagnostic> diags)
    {
        var nsRegex = new Regex(@"namespace\s+([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*\{", RegexOptions.Multiline);
        foreach (Match nsMatch in nsRegex.Matches(source))
        {
            var nsName = nsMatch.Groups[1].Value;
            var nsStart = nsMatch.Index + nsMatch.Length - 1;
            var nsBlock = ReadBalancedBlock(source, nsStart, out _);
            var nsInner = nsBlock[1..^1];
            var baseIndex = nsMatch.Index + nsMatch.Length;

            var effectRegex = new Regex(@"partial\s+effect\s+([A-Za-z_]\w*)\s*\{", RegexOptions.Multiline);
            foreach (Match effectMatch in effectRegex.Matches(nsInner))
            {
                var effectName = effectMatch.Groups[1].Value;
                var effectBraceStartInInner = effectMatch.Index + effectMatch.Length - 1;
                var effectBlock = ReadBalancedBlock(nsInner, effectBraceStartInInner, out _);
                var effectBody = effectBlock[1..^1].Trim();
                var effectStartInSource = baseIndex + effectMatch.Index;
                var effectSpan = GetSpan(source, effectStartInSource);
                var bodySpan = GetSpan(source, baseIndex + effectBraceStartInInner + 1);

                var usingParams = Regex.Matches(effectBody, @"using\s+params\s+([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*;", RegexOptions.Multiline)
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .ToList();
                var mixins = Regex.Matches(effectBody, @"mixin\s+([^;]+);", RegexOptions.Multiline)
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value.Trim())
                    .ToList();

                effects.Add(new SdslEffectBlock(nsName, effectName, usingParams, mixins, effectBody, bodySpan, effectSpan));
                diags.Add(Diagnostic.Create("SD400", $"Partial effect '{effectName}' parsed but effect lowering/artifact generation is not implemented.", effectSpan.Line, effectSpan.Column));
                foreach (var p in usingParams)
                    diags.Add(Diagnostic.Create("SD401", $"using params '{p}' parsed in effect '{effectName}', but parameter binding is not implemented.", effectSpan.Line, effectSpan.Column));
                foreach (var mixin in mixins)
                    diags.Add(Diagnostic.Create("SD402", $"mixin '{mixin}' parsed in effect '{effectName}', but effect composition is not implemented.", effectSpan.Line, effectSpan.Column));
            }
        }
    }

    private static IReadOnlyList<ShaderGenericParameter> ParseGenericParameters(string source, Group genericGroup, string? genericParametersText, List<Diagnostic> diags)
    {
        var parsed = new List<ShaderGenericParameter>();
        if (string.IsNullOrWhiteSpace(genericParametersText) || !genericGroup.Success)
            return parsed;

        var parts = genericParametersText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var running = 0;
        foreach (var part in parts)
        {
            var tokens = part.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 2)
            {
                var span = GetSpan(source, genericGroup.Index + running);
                diags.Add(Diagnostic.Create("SD323", $"Failed to parse generic parameter list '{genericParametersText}'.", span.Line, span.Column));
                return [];
            }

            var localIdx = genericParametersText.IndexOf(part, running, StringComparison.Ordinal);
            if (localIdx < 0) localIdx = running;
            running = localIdx + part.Length;
            parsed.Add(new ShaderGenericParameter(tokens[0], tokens[1], GetSpan(source, genericGroup.Index + localIdx)));
        }

        return parsed;
    }

    private static SourceSpan GetSpan(string source, int index)
    {
        var line = 1;
        var col = 1;
        for (var i = 0; i < index; i++)
        {
            if (source[i] == '\n') { line++; col = 1; }
            else col++;
        }
        return new(index, 1, line, col);
    }

    private static string ReadBalancedBlock(string source, int braceStart, out int end)
    {
        var depth = 0;
        for (var i = braceStart; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
            if (depth == 0) { end = i; return source[braceStart..(i + 1)]; }
        }
        end = source.Length - 1;
        return source[braceStart..];
    }
}
