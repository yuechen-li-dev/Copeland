using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Compiler;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Copeland.TS.MSBuild;

/// <summary>
/// Lowers Copeland test modules into ordinary C# test methods. The generated
/// wrappers are deliberately small: xUnit remains responsible for discovery,
/// execution, filtering, and reporting.
/// </summary>
public sealed class CopelandTestCompile : Microsoft.Build.Utilities.Task
{
    private static readonly Regex AttributeLine = new(
        @"^[ \t]*\[(?<attribute>[^\]\r\n]+)\][ \t]*(?://.*)?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex Function = new(
        @"(?<export>\bexport\s+)?function\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<parameters>[^)]*)\)",
        RegexOptions.CultureInvariant);

    private static readonly Regex Import = new(
        "import\\s*\\{\\s*(?<members>[^}]+)\\s*\\}\\s*from\\s*[\\\"'](?<path>[^\\\"']+)[\\\"']\\s*;?",
        RegexOptions.CultureInvariant);

    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    public ITaskItem[] ClrReferencePaths { get; set; } = [];

    [Required]
    public string IntermediateOutputPath { get; set; } = string.Empty;

    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    public string RootNamespace { get; set; } = "Copeland";

    public string ProjectTypes { get; set; } = string.Empty;

    [Output]
    public ITaskItem[] GeneratedSources { get; private set; } = [];

    public override bool Execute()
    {
        try
        {
            return ExecuteCore();
        }
        catch (Exception exception)
        {
            Exception rootCause = exception.GetBaseException();
            Log.LogError(
                "COPE-MSBUILD-0010",
                "",
                "",
                ProjectDirectory,
                0,
                0,
                0,
                0,
                $"Copeland test compilation failed unexpectedly: {rootCause.GetType().Name}: {rootCause.Message}");
            return false;
        }
    }

    private bool ExecuteCore()
    {
        string projectDirectory = Path.GetFullPath(ProjectDirectory);
        string generatedDirectory = Path.Combine(Path.GetFullPath(IntermediateOutputPath), "CopelandTests");
        Directory.CreateDirectory(generatedDirectory);

        CopelandClrReference[] references = ClrReferencePaths
            .Select(item => item.ItemSpec)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new CopelandClrReference(path))
            .ToArray();
        var generated = new List<ITaskItem>();
        var activeOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ITaskItem source in Sources.OrderBy(item => item.ItemSpec, StringComparer.OrdinalIgnoreCase))
        {
            string sourcePath = Path.GetFullPath(source.ItemSpec, projectDirectory);
            if (!File.Exists(sourcePath))
            {
                Log.LogError("COPE-XUNIT-0001", "", "", sourcePath, 0, 0, 0, 0, "Copeland test input file does not exist.");
                continue;
            }

            string outputName = SanitizeIdentifier(Path.GetFileNameWithoutExtension(sourcePath)) + "_" + ShortHash(sourcePath);
            string outputPath = Path.Combine(generatedDirectory, outputName + ".g.cs");
            activeOutputs.Add(outputPath);
            if (Compile(sourcePath, File.ReadAllText(sourcePath), references, outputName, outputPath))
            {
                generated.Add(new TaskItem(outputPath));
            }
        }

        foreach (string stale in Directory.EnumerateFiles(generatedDirectory, "*.g.cs"))
        {
            if (!activeOutputs.Contains(stale))
            {
                File.Delete(stale);
            }
        }

        GeneratedSources = generated.ToArray();
        return !Log.HasLoggedErrors;
    }

    private bool Compile(
        string sourcePath,
        string sourceText,
        IReadOnlyList<CopelandClrReference> references,
        string outputName,
        string outputPath)
    {
        IReadOnlyList<TestFunction> tests = ParseTests(sourceText, sourcePath);
        if (Log.HasLoggedErrors)
        {
            return false;
        }

        string loweredSource = NormalizeAssertionLiterals(RewriteProductionImports(RemoveAttributes(sourceText), sourcePath));
        string productionUsing = Import.IsMatch(sourceText)
            ? "using " + NormalizeNamespaceForCode(RootNamespace) + ".Copeland;\n"
            : string.Empty;
        string generatedNamespace = NormalizeNamespaceForCode(RootNamespace) + ".CopelandTests.Generated";
        string moduleName = "TestModule_" + outputName;
        string compilerSourcePath = ProjectTypes.Contains("TextDocuments", StringComparison.OrdinalIgnoreCase)
            ? Path.ChangeExtension(sourcePath, ".tsx")
            : sourcePath;
        CopelandCompilation compilation = CopelandCompiler.CompileToMir(
            productionUsing + loweredSource,
            new CopelandCompilationOptions
            {
                SourcePath = compilerSourcePath,
                ProjectRoot = Path.GetDirectoryName(sourcePath),
                ClrReferences = references,
                ProjectTypes = CopelandProjectTypes.FromNames(ProjectTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), out _),
            });
        if (!compilation.Success)
        {
            foreach (var diagnostic in compilation.Diagnostics)
            {
                (int line, int column) = GetLineAndColumn(sourceText, diagnostic.Position);
                Log.LogError(diagnostic.Id, "", "", sourcePath, line, column, line, column + Math.Max(1, diagnostic.Length), diagnostic.Message);
            }

            return false;
        }

        CSharpCompilation emitted = CSharpBackend.Emit(compilation.MirCompilation!.Program!);
        if (emitted.Diagnostics.Count > 0)
        {
            foreach (CSharpDiagnostic diagnostic in emitted.Diagnostics)
            {
                Log.LogError(diagnostic.Id, "", "", sourcePath, 0, 0, 0, 0, diagnostic.Message);
            }

            return false;
        }

        string implementation = emitted.SourceText
            .Replace("namespace Copeland.Generated;", "namespace " + generatedNamespace + "\n{", StringComparison.Ordinal)
            .Replace("public static class CopelandModule", "public static class " + moduleName, StringComparison.Ordinal);
        string wrappers = BuildWrappers(tests, sourcePath, generatedNamespace, moduleName, outputName);
        WriteIfChanged(outputPath, "#line 1 \"" + EscapeLinePath(sourcePath) + "\"\n" + implementation + "\n}\n#line default\n" + wrappers);
        return true;
    }

    private IReadOnlyList<TestFunction> ParseTests(string sourceText, string sourcePath)
    {
        var attributes = new List<TestAttribute>();
        var tests = new List<TestFunction>();
        int cursor = 0;
        foreach (Match function in Function.Matches(sourceText))
        {
            string prefix = sourceText[cursor..function.Index];
            foreach (Match attribute in AttributeLine.Matches(prefix))
            {
                attributes.Add(new TestAttribute(attribute.Groups["attribute"].Value.Trim(), GetLine(sourceText, cursor + attribute.Index)));
            }

            bool isFact = attributes.Any(attribute => IsAttribute(attribute.Text, "Fact"));
            bool isTheory = attributes.Any(attribute => IsAttribute(attribute.Text, "Theory"));
            if (isFact || isTheory)
            {
                string name = function.Groups["name"].Value;
                IReadOnlyList<TestParameter> parameters = ParseParameters(function.Groups["parameters"].Value, sourcePath, GetLine(sourceText, function.Index));
                if (isFact && parameters.Count != 0)
                {
                    Log.LogError("COPE-XUNIT-0002", "", "", sourcePath, GetLine(sourceText, function.Index), 1, GetLine(sourceText, function.Index), 1, $"[Fact] test '{name}' must not declare parameters.");
                }

                if (isTheory && !attributes.Any(attribute => IsAttribute(attribute.Text, "InlineData")))
                {
                    Log.LogError("COPE-XUNIT-0003", "", "", sourcePath, GetLine(sourceText, function.Index), 1, GetLine(sourceText, function.Index), 1, $"[Theory] test '{name}' requires at least one [InlineData(...)] attribute in CTS-XUNIT-M1.");
                }

                foreach (TestAttribute inlineData in attributes.Where(attribute => IsAttribute(attribute.Text, "InlineData")))
                {
                    if (!HasConstantArguments(inlineData.Text))
                    {
                        Log.LogError("COPE-XUNIT-0004", "", "", sourcePath, inlineData.Line, 1, inlineData.Line, 1, "[InlineData] arguments must be compile-time constants.");
                    }
                }

                tests.Add(new TestFunction(name, GetLine(sourceText, function.Index), attributes.ToArray(), parameters));
            }

            attributes.Clear();
            cursor = function.Index + function.Length;
        }

        return tests;
    }

    private static string RemoveAttributes(string source)
        => AttributeLine.Replace(source, match => new string(match.Value.Select(character => character is '\r' or '\n' ? character : ' ').ToArray()));

    private static string RewriteProductionImports(string source, string sourcePath)
    {
        string rewritten = source;
        foreach (Match match in Import.Matches(source))
        {
            string importedPath = match.Groups["path"].Value;
            if (importedPath.EndsWith(".tsxtest", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string moduleName = SanitizeIdentifier(Path.GetFileNameWithoutExtension(importedPath));
            foreach (string member in match.Groups["members"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string name = member.Split(" as ", StringSplitOptions.TrimEntries)[0];
                string localName = member.Contains(" as ", StringComparison.Ordinal) ? member.Split(" as ", StringSplitOptions.TrimEntries)[1] : name;
                rewritten = Regex.Replace(rewritten, @"\b" + Regex.Escape(localName) + @"\s*\(", moduleName + "." + name + "(");
            }

            rewritten = rewritten.Replace(match.Value, string.Empty, StringComparison.Ordinal);
        }

        return rewritten;
    }

    private static string BuildWrappers(
        IReadOnlyList<TestFunction> tests,
        string sourcePath,
        string generatedNamespace,
        string moduleName,
        string outputName)
    {
        var builder = new StringBuilder();
        builder.AppendLine("namespace " + NormalizeNamespaceForCode(generatedNamespace));
        builder.AppendLine("{");
        builder.AppendLine();
        builder.AppendLine("public sealed class " + outputName + "_Tests");
        builder.AppendLine("{");
        foreach (TestFunction test in tests)
        {
            foreach (TestAttribute attribute in test.Attributes)
            {
                builder.AppendLine("    [global::Xunit." + ToAttributeType(attribute.Text) + "]");
            }

            int wrapperLineBase = Math.Max(1, test.Line - 2);
            builder.AppendLine("#line " + wrapperLineBase.ToString(System.Globalization.CultureInfo.InvariantCulture) + " \"" + EscapeLinePath(sourcePath) + "\"");
            builder.Append("    public void ").Append(test.Name).Append('(');
            builder.Append(string.Join(", ", test.Parameters.Select(parameter => parameter.CSharpType + " " + parameter.Name)));
            builder.AppendLine(")");
            builder.AppendLine("    {");
            builder.Append("        ").Append(moduleName).Append('.').Append(test.Name).Append('(');
            builder.Append(string.Join(", ", test.Parameters.Select(parameter => parameter.Name)));
            builder.AppendLine(");");
            builder.AppendLine("    }");
            builder.AppendLine("#line default");
        }

        builder.AppendLine("}");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static IReadOnlyList<TestParameter> ParseParameters(string text, string sourcePath, int line)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var result = new List<TestParameter>();
        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] pieces = part.Split(':', StringSplitOptions.TrimEntries);
            if (pieces.Length != 2) continue;
            string type = pieces[1] switch
            {
                "number" => "float",
                "int" => "int",
                "float" => "float",
                "string" => "string",
                "boolean" => "bool",
                _ => pieces[1],
            };
            result.Add(new TestParameter(SanitizeIdentifier(pieces[0]), type));
        }

        return result;
    }

    private static string NormalizeAssertionLiterals(string source)
    {
        // Copeland's `number` lowers to Single. Give the generic xUnit
        // overload resolver the same type for the common integer-literal
        // expected value form without changing authored test syntax.
        return Regex.Replace(
            source,
            @"\bAssert\.Equal\(\s*(?<value>-?[0-9]+)\s*,",
            match => "Assert.Equal(" + match.Groups["value"].Value + ".0,",
            RegexOptions.CultureInvariant);
    }

    private static bool IsAttribute(string text, string name)
    {
        string typeName = text.Split('(', 2)[0].Trim();
        return string.Equals(typeName, name, StringComparison.Ordinal)
            || string.Equals(typeName, name + "Attribute", StringComparison.Ordinal)
            || typeName.EndsWith("." + name, StringComparison.Ordinal)
            || typeName.EndsWith("." + name + "Attribute", StringComparison.Ordinal);
    }

    private static bool HasConstantArguments(string attribute)
    {
        int open = attribute.IndexOf('(');
        int close = attribute.LastIndexOf(')');
        if (open < 0 || close <= open) return false;
        string[] values = attribute[(open + 1)..close].Split(',', StringSplitOptions.TrimEntries);
        return values.All(value => Regex.IsMatch(value, "^(?:-?[0-9]+(?:\\.[0-9]+)?|true|false|null|\\\"(?:[^\\\"\\\\]|\\\\.)*\\\")$", RegexOptions.CultureInvariant));
    }

    private static string ToAttributeType(string text)
    {
        int open = text.IndexOf('(');
        string type = (open < 0 ? text : text[..open]).Trim();
        string arguments = open < 0 ? string.Empty : text[open..];
        string shortName = type.Split('.').Last();
        if (!shortName.EndsWith("Attribute", StringComparison.Ordinal)) shortName += "Attribute";
        return shortName + arguments;
    }

    private static string NormalizeNamespaceForCode(string value) => string.Join('.', value.Split('.').Select(SanitizeIdentifier));

    private static string SanitizeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length + 1);
        if (value.Length == 0 || (!char.IsLetter(value[0]) && value[0] != '_')) builder.Append('_');
        foreach (char character in value) builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        return builder.ToString();
    }

    private static string ShortHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8];

    private static void WriteIfChanged(string path, string text)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), text, StringComparison.Ordinal)) return;
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static int GetLine(string text, int position) => 1 + text[..Math.Clamp(position, 0, text.Length)].Count(character => character == '\n');

    private static (int Line, int Column) GetLineAndColumn(string text, int position)
    {
        int line = GetLine(text, position);
        if (text.Length == 0) return (line, 1);
        int lastLineStart = text.LastIndexOf('\n', Math.Clamp(position, 0, text.Length - 1));
        return (line, Math.Max(1, position - lastLineStart));
    }

    private static string EscapeLinePath(string path) => path.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed record TestAttribute(string Text, int Line);
    private sealed record TestFunction(string Name, int Line, IReadOnlyList<TestAttribute> Attributes, IReadOnlyList<TestParameter> Parameters);
    private sealed record TestParameter(string Name, string CSharpType);
}
