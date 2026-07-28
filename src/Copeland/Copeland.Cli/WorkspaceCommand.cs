using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Security;
using System.Xml;

namespace Copeland.Cli;

/// <summary>
/// Declarative, file-level compiler partitioning for a TypeScript workspace.
/// The deliberately restricted tsconfig.tsx syntax is data only: literals,
/// arrays, and object records passed to defineTypeScriptWorkspace.
/// </summary>
internal static class WorkspaceCommand
{
    private const string GeneratedDirectoryName = "obj/copeland/workspace";
    private const string GeneratedTsConfigName = "tsconfig.generated.json";
    private const string GeneratedPropsName = "tscl-files.generated.props";
    private const string GeneratedOwnershipName = "editor-ownership.generated.json";

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage("COPE-WORKSPACE-0001", "Missing workspace subcommand. Use sync, validate, status, or owner.");
        }

        string command = args[1];
        if (!TryParseArguments(args[2..], command, out WorkspaceArguments options, out string? argumentError))
        {
            return Usage("COPE-WORKSPACE-0001", argumentError!);
        }

        WorkspaceResult result = LoadAndResolve(options);
        if (!result.Success)
        {
            WriteResult(result, options.Json);
            return 1;
        }

        if (command == "validate")
        {
            WriteResult(result with { Command = "workspace.validate" }, options.Json);
            return 0;
        }

        if (command == "sync")
        {
            try
            {
                WorkspaceArtifacts artifacts = WorkspaceArtifacts.Create(result.Workspace!, result.Ownership!);
                bool changed = artifacts.PublishAtomically();
                WriteResult(result with
                {
                    Command = "workspace.sync",
                    Changed = changed,
                    Artifacts = artifacts.RelativePaths,
                }, options.Json);
                return 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                WorkspaceResult failed = result with
                {
                    Command = "workspace.sync",
                    Diagnostics = [WorkspaceDiagnostic.Error("COPE-WORKSPACE-0018", $"Failed to publish workspace artifacts: {exception.Message}", result.Workspace!.ManifestPath)],
                };
                WriteResult(failed, options.Json);
                return 3;
            }
        }

        if (command == "status")
        {
            WriteStatus(result with { Command = "workspace.status" }, options.Json);
            return 0;
        }

        if (command == "owner")
        {
            string requestedPath = options.OwnerPath!;
            string normalized = NormalizeRequestedPath(result.Workspace!, requestedPath);
            WorkspaceOwnedFile? ownedFile = result.Ownership!.Files.SingleOrDefault(file => file.Path == normalized);
            if (ownedFile is null)
            {
                WorkspaceResult unknown = result with
                {
                    Command = "workspace.owner",
                    Diagnostics = [WorkspaceDiagnostic.Error("COPE-WORKSPACE-0017", $"No owned TypeScript source exists at '{requestedPath}'.", result.Workspace!.ManifestPath)],
                };
                WriteResult(unknown, options.Json);
                return 1;
            }

            WriteOwner(result with { Command = "workspace.owner" }, ownedFile, options.Json);
            return 0;
        }

        return Usage("COPE-WORKSPACE-0001", $"Unknown workspace subcommand '{command}'. Use sync, validate, status, or owner.");
    }

    private static bool TryParseArguments(string[] args, string command, out WorkspaceArguments options, out string? error)
    {
        string workspacePath = Path.Combine(Environment.CurrentDirectory, "tsconfig.tsx");
        string? ownerPath = null;
        bool json = false;
        error = null;

        for (int index = 0; index < args.Length; index += 1)
        {
            if (args[index] == "--workspace" && index + 1 < args.Length)
            {
                workspacePath = args[++index];
                continue;
            }

            if (args[index] == "--format" && index + 1 < args.Length)
            {
                string format = args[++index];
                if (format is not "text" and not "json")
                {
                    options = default!;
                    error = "Option '--format' must be 'text' or 'json'.";
                    return false;
                }

                json = format == "json";
                continue;
            }

            if (args[index].StartsWith("--", StringComparison.Ordinal))
            {
                options = default!;
                error = $"Unknown or incomplete option '{args[index]}'.";
                return false;
            }

            if (command == "owner" && ownerPath is null)
            {
                ownerPath = args[index];
                continue;
            }

            options = default!;
            error = $"Unexpected argument '{args[index]}'.";
            return false;
        }

        if (command == "owner" && string.IsNullOrWhiteSpace(ownerPath))
        {
            options = default!;
            error = "Usage: tscl workspace owner <source-path> [--format text|json].";
            return false;
        }

        options = new WorkspaceArguments(Path.GetFullPath(workspacePath), ownerPath, json);
        return true;
    }

    private static WorkspaceResult LoadAndResolve(WorkspaceArguments options)
    {
        if (!File.Exists(options.WorkspacePath))
        {
            return WorkspaceResult.Failure("workspace", WorkspaceDiagnostic.Error("COPE-WORKSPACE-0002", "Workspace manifest 'tsconfig.tsx' does not exist.", options.WorkspacePath));
        }

        try
        {
            string source = File.ReadAllText(options.WorkspacePath);
            WorkspaceParser parser = new(options.WorkspacePath, source);
            WorkspaceManifest? workspace = parser.Parse();
            if (workspace is null)
            {
                return WorkspaceResult.Failure("workspace", parser.Diagnostics);
            }

            WorkspaceResolver resolver = new(workspace);
            WorkspaceOwnership ownership = resolver.Resolve();
            return resolver.Diagnostics.Count == 0
                ? WorkspaceResult.Successful(workspace, ownership)
                : WorkspaceResult.Failure("workspace", resolver.Diagnostics, workspace, ownership);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return WorkspaceResult.Failure("workspace", WorkspaceDiagnostic.Error("COPE-WORKSPACE-0003", exception.Message, options.WorkspacePath));
        }
    }

    private static string NormalizeRequestedPath(WorkspaceManifest workspace, string path)
    {
        string fullPath = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(path, workspace.RootDirectory);
        return Path.GetRelativePath(workspace.RootDirectory, fullPath).Replace('\\', '/');
    }

    private static void WriteStatus(WorkspaceResult result, bool json)
    {
        if (json)
        {
            WriteResult(result, true);
            return;
        }

        foreach (WorkspaceRule rule in result.Workspace!.Rules)
        {
            int count = result.Ownership!.Files.Count(file => file.Owner == rule.Owner && file.Rule.Include == rule.Include);
            Console.Out.WriteLine($"{rule.Owner}");
            Console.Out.WriteLine($"  {rule.Include,-24} {count,4} files");
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine($"unowned                 {result.Ownership!.UnownedCount,4}");
        Console.Out.WriteLine($"overlapping             {result.Ownership.OverlappingCount,4}");
    }

    private static void WriteOwner(WorkspaceResult result, WorkspaceOwnedFile file, bool json)
    {
        if (json)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                success = true,
                command = result.Command,
                path = file.Path,
                owner = file.Owner,
                project = file.Project,
                matchedRule = file.Rule.Include,
            }, JsonOptions));
            return;
        }

        Console.Out.WriteLine(file.Path);
        Console.Out.WriteLine($"owner: {file.Owner}");
        Console.Out.WriteLine($"project: {file.Project}");
        Console.Out.WriteLine($"matched rule: {file.Rule.Include}");
    }

    private static void WriteResult(WorkspaceResult result, bool json)
    {
        if (!json)
        {
            foreach (WorkspaceDiagnostic diagnostic in result.Diagnostics)
            {
                Console.Error.WriteLine($"{diagnostic.Code} {diagnostic.Severity}: {diagnostic.Message}");
            }

            if (result.Success)
            {
                Console.Out.WriteLine(result.Changed ? "workspace artifacts synchronized" : "workspace is valid");
            }

            return;
        }

        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            success = result.Success,
            command = result.Command,
            changed = result.Changed,
            diagnostics = result.Diagnostics,
            artifacts = result.Artifacts,
        }, JsonOptions));
    }

    private static int Usage(string code, string message)
    {
        Console.Error.WriteLine("Usage: tscl workspace sync|validate|status [--workspace <tsconfig.tsx>] [--format text|json]");
        Console.Error.WriteLine("       tscl workspace owner <source-path> [--workspace <tsconfig.tsx>] [--format text|json]");
        Console.Error.WriteLine($"{code} error: {message}");
        return 2;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed record WorkspaceArguments(string WorkspacePath, string? OwnerPath, bool Json);
}

internal sealed record WorkspaceManifest(
    string ManifestPath,
    string RootDirectory,
    bool StrictOwnership,
    WorkspaceCompiler? Tsc,
    WorkspaceCompiler? Tscl,
    IReadOnlyList<WorkspaceRule> Rules);

internal sealed record WorkspaceCompiler(string Owner, string? Project, IReadOnlyList<string> Include, IReadOnlyList<string> Exclude, JsonElement? CompilerOptions);
internal sealed record WorkspaceRule(string Owner, string Include, IReadOnlyList<string> Exclude, string Project);
internal sealed record WorkspaceOwnedFile(string Path, string Owner, string Project, WorkspaceRule Rule);
internal sealed record WorkspaceOwnership(IReadOnlyList<WorkspaceOwnedFile> Files, int UnownedCount, int OverlappingCount);
internal sealed record WorkspaceDiagnostic(string Code, string Severity, string Message, string File, int? Line = null, int? Column = null)
{
    public static WorkspaceDiagnostic Error(string code, string message, string file) => new(code, "error", message, file);
}

internal sealed record WorkspaceResult(
    bool Success,
    string Command,
    IReadOnlyList<WorkspaceDiagnostic> Diagnostics,
    WorkspaceManifest? Workspace,
    WorkspaceOwnership? Ownership,
    bool Changed = false,
    IReadOnlyList<string>? Artifacts = null)
{
    public static WorkspaceResult Successful(WorkspaceManifest workspace, WorkspaceOwnership ownership)
        => new(true, "workspace", [], workspace, ownership);

    public static WorkspaceResult Failure(string command, WorkspaceDiagnostic diagnostic, WorkspaceManifest? workspace = null, WorkspaceOwnership? ownership = null)
        => new(false, command, [diagnostic], workspace, ownership);

    public static WorkspaceResult Failure(string command, IReadOnlyList<WorkspaceDiagnostic> diagnostics, WorkspaceManifest? workspace = null, WorkspaceOwnership? ownership = null)
        => new(false, command, diagnostics, workspace, ownership);
}

internal sealed class WorkspaceParser
{
    private readonly string _path;
    private readonly string _source;
    private readonly WorkspaceValueParser _values;

    public WorkspaceParser(string path, string source)
    {
        _path = path;
        _source = source;
        _values = new WorkspaceValueParser(path, source);
    }

    public IReadOnlyList<WorkspaceDiagnostic> Diagnostics => _values.Diagnostics;

    public WorkspaceManifest? Parse()
    {
        int invocation = _source.IndexOf("defineTypeScriptWorkspace", StringComparison.Ordinal);
        if (invocation < 0)
        {
            _values.Report("COPE-WORKSPACE-0004", "Workspace manifest must call defineTypeScriptWorkspace with one object literal.", 0);
            return null;
        }

        int openParen = _source.IndexOf('(', invocation);
        if (openParen < 0)
        {
            _values.Report("COPE-WORKSPACE-0004", "Workspace manifest call is incomplete.", invocation);
            return null;
        }

        WorkspaceValue? root = _values.ParseValue(openParen + 1);
        if (root is not WorkspaceObject rootObject)
        {
            _values.Report("COPE-WORKSPACE-0004", "defineTypeScriptWorkspace requires an object literal.", openParen + 1);
            return null;
        }

        HashSet<string> allowedRoot = ["ownership", "tsc", "tscl"];
        foreach (string key in rootObject.Properties.Keys)
        {
            if (!allowedRoot.Contains(key))
            {
                _values.Report("COPE-WORKSPACE-0005", $"Unknown workspace property '{key}'.", rootObject.PropertyOffsets[key]);
            }
        }

        bool strict = true;
        if (rootObject.Properties.TryGetValue("ownership", out WorkspaceValue? policy))
        {
            if (policy is not WorkspaceString { Value: "strict" or "partial" } ownership)
            {
                _values.Report("COPE-WORKSPACE-0006", "ownership must be 'strict' or 'partial'.", policy.Offset);
            }
            else
            {
                strict = ownership.Value == "strict";
            }
        }

        WorkspaceCompiler? tsc = BindCompiler(rootObject, "tsc", false);
        WorkspaceCompiler? tscl = BindCompiler(rootObject, "tscl", true);
        if (tsc is null && tscl is null)
        {
            _values.Report("COPE-WORKSPACE-0007", "Workspace must declare at least one compiler owner: tsc or tscl.", rootObject.Offset);
        }

        if (_values.Diagnostics.Count != 0)
        {
            return null;
        }

        var rules = new List<WorkspaceRule>();
        AddRules(tsc, rules);
        AddRules(tscl, rules);
        return new WorkspaceManifest(_path, Path.GetDirectoryName(_path)!, strict, tsc, tscl, rules);
    }

    private WorkspaceCompiler? BindCompiler(WorkspaceObject root, string owner, bool requiresProject)
    {
        if (!root.Properties.TryGetValue(owner, out WorkspaceValue? value))
        {
            return null;
        }

        if (value is not WorkspaceObject config)
        {
            _values.Report("COPE-WORKSPACE-0008", $"{owner} must be an object literal.", value.Offset);
            return null;
        }

        HashSet<string> allowed = owner == "tsc"
            ? ["include", "exclude", "compilerOptions"]
            : ["project", "include", "exclude"];
        foreach (string key in config.Properties.Keys)
        {
            if (!allowed.Contains(key))
            {
                _values.Report("COPE-WORKSPACE-0005", $"Unknown {owner} property '{key}'.", config.PropertyOffsets[key]);
            }
        }

        if (!TryStringArray(config, "include", required: true, out IReadOnlyList<string> include) || include.Count == 0)
        {
            _values.Report("COPE-WORKSPACE-0009", $"{owner}.include must be a non-empty array of strings.", config.Offset);
        }

        TryStringArray(config, "exclude", required: false, out IReadOnlyList<string> exclude);
        foreach (string pattern in include.Concat(exclude))
        {
            if (!WorkspaceGlob.TryNormalize(pattern, out _, out string? error))
            {
                _values.Report("COPE-WORKSPACE-0010", $"Invalid glob '{pattern}': {error}", config.Offset);
            }
        }

        string? project = null;
        if (requiresProject)
        {
            if (!config.Properties.TryGetValue("project", out WorkspaceValue? projectValue) || projectValue is not WorkspaceString projectString)
            {
                _values.Report("COPE-WORKSPACE-0011", "tscl.project must be a relative .csproj path.", config.Offset);
            }
            else if (!WorkspaceGlob.TryNormalizePath(projectString.Value, out project, out _)
                     || !project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                _values.Report("COPE-WORKSPACE-0011", "tscl.project must be a safe relative .csproj path.", projectValue.Offset);
            }
        }

        JsonElement? compilerOptions = null;
        if (owner == "tsc" && config.Properties.TryGetValue("compilerOptions", out WorkspaceValue? options))
        {
            if (options is not WorkspaceObject optionsObject)
            {
                _values.Report("COPE-WORKSPACE-0012", "tsc.compilerOptions must be an object literal.", options.Offset);
            }
            else
            {
                compilerOptions = BindCompilerOptions(optionsObject);
            }
        }

        return new WorkspaceCompiler(owner, project, include, exclude, compilerOptions);
    }

    private JsonElement? BindCompilerOptions(WorkspaceObject options)
    {
        HashSet<string> supported = [
            "target", "module", "moduleResolution", "strict", "jsx", "jsxImportSource", "lib", "types", "baseUrl", "paths", "rootDir", "outDir",
            "declaration", "sourceMap", "esModuleInterop", "skipLibCheck", "allowJs", "checkJs", "resolveJsonModule",
        ];
        foreach (string key in options.Properties.Keys)
        {
            if (!supported.Contains(key))
            {
                _values.Report("COPE-WORKSPACE-0013", $"Unsupported tsc compiler option '{key}' in M0.", options.PropertyOffsets[key]);
            }
        }

        foreach ((string key, WorkspaceValue optionValue) in options.Properties)
        {
            if (!IsValidCompilerOptionValue(key, optionValue))
            {
                _values.Report("COPE-WORKSPACE-0012", $"Compiler option '{key}' has an invalid value shape.", optionValue.Offset);
            }
        }

        object? value = options.ToPlainValue(_values, validate: true);
        if (value is null)
        {
            return null;
        }

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(value);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static bool IsValidCompilerOptionValue(string name, WorkspaceValue value)
    {
        if (name is "strict" or "declaration" or "sourceMap" or "esModuleInterop" or "skipLibCheck" or "allowJs" or "checkJs" or "resolveJsonModule")
        {
            return value is WorkspaceBoolean;
        }

        if (name is "lib" or "types")
        {
            return value is WorkspaceArray { Values: var items } && items.All(item => item is WorkspaceString);
        }

        if (name == "paths")
        {
            return value is WorkspaceObject { Properties: var paths }
                && paths.Values.All(item => item is WorkspaceArray { Values: var items } && items.All(entry => entry is WorkspaceString));
        }

        return value is WorkspaceString;
    }

    private bool TryStringArray(WorkspaceObject value, string name, bool required, out IReadOnlyList<string> strings)
    {
        strings = [];
        if (!value.Properties.TryGetValue(name, out WorkspaceValue? member))
        {
            return !required;
        }

        if (member is not WorkspaceArray array || array.Values.Any(item => item is not WorkspaceString))
        {
            _values.Report("COPE-WORKSPACE-0014", $"{name} must be an array of strings.", member.Offset);
            return false;
        }

        strings = array.Values.Cast<WorkspaceString>().Select(item => item.Value).ToArray();
        return true;
    }

    private static void AddRules(WorkspaceCompiler? compiler, ICollection<WorkspaceRule> rules)
    {
        if (compiler is null)
        {
            return;
        }

        string project = compiler.Owner == "tscl" ? compiler.Project! : "obj/copeland/workspace/tsconfig.generated.json";
        foreach (string include in compiler.Include)
        {
            rules.Add(new WorkspaceRule(compiler.Owner, WorkspaceGlob.Normalize(include), compiler.Exclude.Select(WorkspaceGlob.Normalize).ToArray(), project));
        }
    }
}

internal abstract record WorkspaceValue(int Offset)
{
    public virtual object? ToPlainValue(WorkspaceValueParser parser, bool validate) => null;
}

internal sealed record WorkspaceString(string Value, int SourceOffset) : WorkspaceValue(SourceOffset)
{
    public override object ToPlainValue(WorkspaceValueParser parser, bool validate) => Value;
}

internal sealed record WorkspaceBoolean(bool Value, int SourceOffset) : WorkspaceValue(SourceOffset)
{
    public override object ToPlainValue(WorkspaceValueParser parser, bool validate) => Value;
}

internal sealed record WorkspaceArray(IReadOnlyList<WorkspaceValue> Values, int SourceOffset) : WorkspaceValue(SourceOffset)
{
    public override object? ToPlainValue(WorkspaceValueParser parser, bool validate)
    {
        var values = new List<object?>();
        foreach (WorkspaceValue value in Values)
        {
            object? plain = value.ToPlainValue(parser, validate);
            if (plain is null)
            {
                parser.Report("COPE-WORKSPACE-0015", "Compiler options must contain only literals, arrays, and records.", value.Offset);
            }

            values.Add(plain);
        }

        return values;
    }
}

internal sealed record WorkspaceObject(Dictionary<string, WorkspaceValue> Properties, Dictionary<string, int> PropertyOffsets, int SourceOffset) : WorkspaceValue(SourceOffset)
{
    public override object? ToPlainValue(WorkspaceValueParser parser, bool validate)
    {
        var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string key, WorkspaceValue value) in Properties)
        {
            object? plain = value.ToPlainValue(parser, validate);
            if (plain is null)
            {
                parser.Report("COPE-WORKSPACE-0015", "Compiler options must contain only literals, arrays, and records.", value.Offset);
            }

            values[key] = plain;
        }

        return values;
    }
}

internal sealed class WorkspaceValueParser
{
    private readonly string _path;
    private readonly string _source;
    private int _position;
    private readonly List<WorkspaceDiagnostic> _diagnostics = [];

    public WorkspaceValueParser(string path, string source)
    {
        _path = path;
        _source = source;
    }

    public IReadOnlyList<WorkspaceDiagnostic> Diagnostics => _diagnostics;

    public WorkspaceValue? ParseValue(int start)
    {
        _position = start;
        SkipTrivia();
        return ReadValue();
    }

    public void Report(string code, string message, int offset)
    {
        int line = 1;
        int column = 1;
        for (int index = 0; index < Math.Min(offset, _source.Length); index += 1)
        {
            if (_source[index] == '\n')
            {
                line += 1;
                column = 1;
            }
            else
            {
                column += 1;
            }
        }

        _diagnostics.Add(new WorkspaceDiagnostic(code, "error", message, _path, line, column));
    }

    private WorkspaceValue? ReadValue()
    {
        SkipTrivia();
        if (_position >= _source.Length)
        {
            Report("COPE-WORKSPACE-0004", "Unexpected end of workspace manifest.", _position);
            return null;
        }

        return _source[_position] switch
        {
            '{' => ReadObject(),
            '[' => ReadArray(),
            '\'' or '"' => ReadString(),
            _ => ReadLiteral(),
        };
    }

    private WorkspaceObject ReadObject()
    {
        int offset = _position++;
        var properties = new Dictionary<string, WorkspaceValue>(StringComparer.Ordinal);
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        while (true)
        {
            SkipTrivia();
            if (Consume('}'))
            {
                return new WorkspaceObject(properties, offsets, offset);
            }

            int keyOffset = _position;
            string? key = ReadKey();
            if (key is null)
            {
                Report("COPE-WORKSPACE-0004", "Expected an object property name.", _position);
                return new WorkspaceObject(properties, offsets, offset);
            }

            SkipTrivia();
            if (!Consume(':'))
            {
                Report("COPE-WORKSPACE-0004", "Expected ':' after object property name.", _position);
                return new WorkspaceObject(properties, offsets, offset);
            }

            WorkspaceValue? value = ReadValue();
            if (value is not null)
            {
                if (!properties.TryAdd(key, value))
                {
                    Report("COPE-WORKSPACE-0016", $"Duplicate workspace property '{key}'.", keyOffset);
                }
                else
                {
                    offsets.Add(key, keyOffset);
                }
            }

            SkipTrivia();
            if (Consume('}'))
            {
                return new WorkspaceObject(properties, offsets, offset);
            }

            if (!Consume(','))
            {
                Report("COPE-WORKSPACE-0004", "Expected ',' or '}' in object literal.", _position);
                return new WorkspaceObject(properties, offsets, offset);
            }
        }
    }

    private WorkspaceArray ReadArray()
    {
        int offset = _position++;
        var values = new List<WorkspaceValue>();
        while (true)
        {
            SkipTrivia();
            if (Consume(']'))
            {
                return new WorkspaceArray(values, offset);
            }

            WorkspaceValue? value = ReadValue();
            if (value is not null)
            {
                values.Add(value);
            }

            SkipTrivia();
            if (Consume(']'))
            {
                return new WorkspaceArray(values, offset);
            }

            if (!Consume(','))
            {
                Report("COPE-WORKSPACE-0004", "Expected ',' or ']' in array literal.", _position);
                return new WorkspaceArray(values, offset);
            }
        }
    }

    private WorkspaceString? ReadString()
    {
        int offset = _position;
        char quote = _source[_position++];
        var value = new StringBuilder();
        while (_position < _source.Length && _source[_position] != quote)
        {
            if (_source[_position] == '\\' && _position + 1 < _source.Length)
            {
                _position += 1;
                value.Append(_source[_position++] switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => _source[_position - 1] });
                continue;
            }

            value.Append(_source[_position++]);
        }

        if (!Consume(quote))
        {
            Report("COPE-WORKSPACE-0004", "Unterminated string literal.", offset);
            return null;
        }

        return new WorkspaceString(value.ToString(), offset);
    }

    private WorkspaceValue? ReadLiteral()
    {
        int offset = _position;
        string identifier = ReadIdentifier();
        return identifier switch
        {
            "true" => new WorkspaceBoolean(true, offset),
            "false" => new WorkspaceBoolean(false, offset),
            _ => ReportAndReturnNull("COPE-WORKSPACE-0015", "Workspace values must be declarative literals, arrays, or records; expressions and function calls are not allowed.", offset),
        };
    }

    private WorkspaceValue? ReportAndReturnNull(string code, string message, int offset)
    {
        Report(code, message, offset);
        return null;
    }

    private string? ReadKey()
    {
        if (_position < _source.Length && _source[_position] is '\'' or '"')
        {
            return ReadString()?.Value;
        }

        string identifier = ReadIdentifier();
        return identifier.Length == 0 ? null : identifier;
    }

    private string ReadIdentifier()
    {
        int start = _position;
        while (_position < _source.Length && (char.IsLetterOrDigit(_source[_position]) || _source[_position] is '_' or '$'))
        {
            _position += 1;
        }

        return _source[start.._position];
    }

    private void SkipTrivia()
    {
        while (_position < _source.Length)
        {
            if (char.IsWhiteSpace(_source[_position]))
            {
                _position += 1;
                continue;
            }

            if (_position + 1 < _source.Length && _source[_position] == '/' && _source[_position + 1] == '/')
            {
                _position += 2;
                while (_position < _source.Length && _source[_position] != '\n')
                {
                    _position += 1;
                }

                continue;
            }

            if (_position + 1 < _source.Length && _source[_position] == '/' && _source[_position + 1] == '*')
            {
                int end = _source.IndexOf("*/", _position + 2, StringComparison.Ordinal);
                _position = end < 0 ? _source.Length : end + 2;
                continue;
            }

            return;
        }
    }

    private bool Consume(char character)
    {
        if (_position < _source.Length && _source[_position] == character)
        {
            _position += 1;
            return true;
        }

        return false;
    }
}

internal sealed class WorkspaceResolver
{
    private readonly WorkspaceManifest _workspace;
    private readonly List<WorkspaceDiagnostic> _diagnostics = [];

    public WorkspaceResolver(WorkspaceManifest workspace) => _workspace = workspace;

    public IReadOnlyList<WorkspaceDiagnostic> Diagnostics => _diagnostics;

    public WorkspaceOwnership Resolve()
    {
        ValidateProjects();
        ValidateGeneratedOutputExclusions();
        var owned = new List<WorkspaceOwnedFile>();
        int unowned = 0;
        int overlapping = 0;
        foreach (string path in EnumerateSources())
        {
            WorkspaceRule[] matches = _workspace.Rules.Where(rule => WorkspaceGlob.IsMatch(path, rule.Include)).ToArray();
            WorkspaceRule[] includedAndExcluded = matches.Where(rule => rule.Exclude.Any(exclude => WorkspaceGlob.IsMatch(path, exclude))).ToArray();
            foreach (WorkspaceRule contradiction in includedAndExcluded)
            {
                _diagnostics.Add(WorkspaceDiagnostic.Error("COPE-WORKSPACE-0020", $"Source '{path}' matches both {contradiction.Owner}.include and {contradiction.Owner}.exclude.", _workspace.ManifestPath));
            }

            matches = matches.Except(includedAndExcluded).ToArray();
            string[] owners = matches.Select(rule => rule.Owner).Distinct(StringComparer.Ordinal).ToArray();
            if (owners.Length > 1)
            {
                overlapping += 1;
                _diagnostics.Add(WorkspaceDiagnostic.Error("COPE-WORKSPACE-0021", $"Source file '{path}' is owned by both tsc and tscl.", _workspace.ManifestPath));
                continue;
            }

            if (owners.Length == 0)
            {
                unowned += 1;
                if (_workspace.StrictOwnership)
                {
                    _diagnostics.Add(WorkspaceDiagnostic.Error("COPE-WORKSPACE-0022", $"Source file '{path}' has no compiler owner.", _workspace.ManifestPath));
                }

                continue;
            }

            WorkspaceRule rule = matches.Single();
            owned.Add(new WorkspaceOwnedFile(path, rule.Owner, rule.Project, rule));
        }

        WorkspaceOwnedFile[] ordered = owned.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        DetectDirectCrossCompilerImports(ordered);
        return new WorkspaceOwnership(ordered, unowned, overlapping);
    }

    private void ValidateProjects()
    {
        if (_workspace.Tscl is null)
        {
            return;
        }

        string fullProject = Path.GetFullPath(_workspace.Tscl.Project!, _workspace.RootDirectory);
        if (!File.Exists(fullProject))
        {
            _diagnostics.Add(WorkspaceDiagnostic.Error("COPE-WORKSPACE-0023", $"tscl project '{_workspace.Tscl.Project}' does not exist.", _workspace.ManifestPath));
        }
    }

    private void ValidateGeneratedOutputExclusions()
    {
        const string generatedProbe = "obj/copeland/workspace/generated.ts";
        foreach (WorkspaceRule rule in _workspace.Rules)
        {
            if (WorkspaceGlob.IsMatch(generatedProbe, rule.Include) && !rule.Exclude.Any(exclude => WorkspaceGlob.IsMatch(generatedProbe, exclude)))
            {
                _diagnostics.Add(WorkspaceDiagnostic.Error("COPE-WORKSPACE-0024", $"{rule.Owner}.include '{rule.Include}' includes generated output paths. Add an explicit exclusion.", _workspace.ManifestPath));
            }
        }
    }

    private void DetectDirectCrossCompilerImports(IReadOnlyList<WorkspaceOwnedFile> owned)
    {
        var byPath = owned.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        Regex importPattern = new("(?:import|export)\\s+(?:[^\\\"']+?\\s+from\\s+)?[\\\"'](?<path>\\.[^\\\"']+)[\\\"']", RegexOptions.CultureInvariant);
        foreach (WorkspaceOwnedFile source in owned)
        {
            string fullPath = Path.Combine(_workspace.RootDirectory, source.Path.Replace('/', Path.DirectorySeparatorChar));
            string sourceText = File.ReadAllText(fullPath);
            foreach (Match match in importPattern.Matches(sourceText))
            {
                string? targetPath = ResolveRelativeModule(source.Path, match.Groups["path"].Value, byPath);
                if (targetPath is not null && byPath[targetPath].Owner != source.Owner)
                {
                    _diagnostics.Add(WorkspaceDiagnostic.Error("COPE-WORKSPACE-0025", $"{source.Owner} source '{source.Path}' imports {byPath[targetPath].Owner}-owned source '{targetPath}' directly. Cross-compiler imports must use emitted artifacts or declared contracts.", _workspace.ManifestPath));
                }
            }
        }
    }

    private static string? ResolveRelativeModule(string sourcePath, string moduleSpecifier, IReadOnlyDictionary<string, WorkspaceOwnedFile> owned)
    {
        string directory = Path.GetDirectoryName(sourcePath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        string basePath = Path.GetFullPath(Path.Combine(Path.DirectorySeparatorChar.ToString(), directory, moduleSpecifier));
        string relative = basePath.TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
        foreach (string candidate in new[] { relative, relative + ".ts", relative + ".tsx", relative + "/index.ts", relative + "/index.tsx" })
        {
            if (owned.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private IEnumerable<string> EnumerateSources()
    {
        foreach (string fullPath in Directory.EnumerateFiles(_workspace.RootDirectory, "*", SearchOption.AllDirectories))
        {
            string path = Path.GetRelativePath(_workspace.RootDirectory, fullPath).Replace('\\', '/');
            if (!path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (path == "tsconfig.tsx" || WorkspaceGlob.IsGeneratedOrVendor(path))
            {
                continue;
            }

            yield return path;
        }
    }
}

internal static class WorkspaceGlob
{
    public static bool TryNormalize(string pattern, out string normalized, out string? error)
    {
        normalized = string.Empty;
        error = null;
        if (!TryNormalizePath(pattern, out normalized, out error))
        {
            return false;
        }

        if (normalized.Contains('[') || normalized.Contains('{') || normalized.Contains('}'))
        {
            error = "character classes and brace expansion are not supported";
            return false;
        }

        return true;
    }

    public static string Normalize(string pattern)
    {
        _ = TryNormalize(pattern, out string normalized, out _);
        return normalized;
    }

    public static bool TryNormalizePath(string path, out string normalized, out string? error)
    {
        normalized = path.Replace('\\', '/').TrimStart('.', '/');
        error = null;
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(path) || normalized.Split('/').Any(part => part is ".." or ""))
        {
            error = "path must be a non-empty workspace-relative path";
            return false;
        }

        if (normalized.Contains('\0'))
        {
            error = "path contains a null character";
            return false;
        }

        return true;
    }

    public static bool IsMatch(string path, string glob)
    {
        string regex = "^" + Regex.Escape(glob)
            .Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal)
            .Replace(@"\*\*", ".*", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal)
            .Replace(@"\?", "[^/]", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(path, regex, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    public static bool IsGeneratedOrVendor(string path)
    {
        return path.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("node_modules/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("dist/", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class WorkspaceArtifacts
{
    private readonly WorkspaceManifest _workspace;
    private readonly WorkspaceOwnership _ownership;
    private readonly string _directory;
    private readonly IReadOnlyDictionary<string, string> _contents;

    private WorkspaceArtifacts(WorkspaceManifest workspace, WorkspaceOwnership ownership)
    {
        _workspace = workspace;
        _ownership = ownership;
        _directory = Path.Combine(workspace.RootDirectory, "obj", "copeland", "workspace");
        _contents = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["tsconfig.generated.json"] = WriteTsConfig(),
            ["tscl-files.generated.props"] = WriteProps(),
            ["editor-ownership.generated.json"] = WriteOwnership(),
        };
    }

    public IReadOnlyList<string> RelativePaths => _contents.Keys.Select(name => "obj/copeland/workspace/" + name).ToArray();

    public static WorkspaceArtifacts Create(WorkspaceManifest workspace, WorkspaceOwnership ownership) => new(workspace, ownership);

    public bool PublishAtomically()
    {
        if (_contents.All(pair => File.Exists(Path.Combine(_directory, pair.Key)) && File.ReadAllText(Path.Combine(_directory, pair.Key)) == pair.Value))
        {
            return false;
        }

        string parent = Path.GetDirectoryName(_directory)!;
        Directory.CreateDirectory(parent);
        string staging = _directory + ".staging-" + Guid.NewGuid().ToString("N");
        string backup = _directory + ".previous-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(staging);
            foreach ((string name, string content) in _contents)
            {
                File.WriteAllText(Path.Combine(staging, name), content, new UTF8Encoding(false));
            }

            if (Directory.Exists(_directory))
            {
                Directory.Move(_directory, backup);
            }

            try
            {
                Directory.Move(staging, _directory);
            }
            catch
            {
                if (Directory.Exists(backup) && !Directory.Exists(_directory))
                {
                    Directory.Move(backup, _directory);
                }

                throw;
            }

            if (Directory.Exists(backup))
            {
                Directory.Delete(backup, recursive: true);
            }

            return true;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private string WriteTsConfig()
    {
        WorkspaceOwnedFile[] files = _ownership.Files.Where(file => file.Owner == "tsc").ToArray();
        var root = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["$schema"] = "https://json.schemastore.org/tsconfig",
            ["compilerOptions"] = ProjectRelativeCompilerOptions(),
            ["exclude"] = BuildGeneratedExcludes(),
            ["files"] = files.Select(file => ProjectRelative(file.Path)).ToArray(),
        };
        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    private JsonObject ProjectRelativeCompilerOptions()
    {
        JsonObject options = _workspace.Tsc?.CompilerOptions is JsonElement compilerOptions
            ? JsonNode.Parse(compilerOptions.GetRawText())!.AsObject()
            : [];
        foreach (string propertyName in new[] { "baseUrl", "rootDir", "outDir" })
        {
            if (options[propertyName] is JsonValue value && value.TryGetValue<string>(out string? path))
            {
                options[propertyName] = ProjectRelative(path!);
            }
        }

        if (options["paths"] is JsonObject paths)
        {
            foreach ((string _, JsonNode? node) in paths.ToArray())
            {
                if (node is not JsonArray entries)
                {
                    continue;
                }

                for (int index = 0; index < entries.Count; index += 1)
                {
                    if (entries[index] is JsonValue entry && entry.TryGetValue<string>(out string? path))
                    {
                        entries[index] = ProjectRelative(path!);
                    }
                }
            }
        }

        return options;
    }

    private string[] BuildGeneratedExcludes()
    {
        var excluded = new SortedSet<string>(StringComparer.Ordinal)
        {
            ProjectRelative("obj/**"), ProjectRelative("bin/**"), ProjectRelative("dist/**"), ProjectRelative("node_modules/**"),
        };
        if (_workspace.Tscl is not null)
        {
            foreach (string pattern in _workspace.Tscl.Include)
            {
                excluded.Add(ProjectRelative(WorkspaceGlob.Normalize(pattern)));
            }
        }

        return excluded.ToArray();
    }

    private string ProjectRelative(string workspaceRelativePath)
    {
        string configDirectory = Path.Combine(_workspace.RootDirectory, "obj", "copeland", "workspace");
        string fullPath = Path.GetFullPath(workspaceRelativePath, _workspace.RootDirectory);
        return Path.GetRelativePath(configDirectory, fullPath).Replace('\\', '/');
    }

    private string WriteProps()
    {
        var builder = new StringBuilder();
        builder.AppendLine("<Project>");
        builder.AppendLine("  <!-- Generated by tscl workspace sync. Import this file from the declared tscl project. -->");
        WorkspaceOwnedFile[] files = _ownership.Files.Where(file => file.Owner == "tscl").ToArray();
        if (files.Length > 0)
        {
            builder.AppendLine("  <ItemGroup>");
            foreach (WorkspaceOwnedFile file in files)
            {
                builder.Append("    <CopelandCompile Include=\"");
                builder.Append(XmlEscape(ProjectRelativeToTsclProject(file.Path).Replace('/', '\\')));
                builder.AppendLine("\" />");
            }

            builder.AppendLine("  </ItemGroup>");
        }

        builder.AppendLine("</Project>");
        return builder.ToString();
    }

    private string WriteOwnership()
    {
        object document = new
        {
            schemaVersion = 1,
            workspaceRoot = ".",
            files = _ownership.Files.Select(file => new { path = file.Path, owner = file.Owner, project = file.Project, matchedRule = file.Rule.Include }),
            rules = _workspace.Rules.Select(rule => new { owner = rule.Owner, include = rule.Include, exclude = rule.Exclude, project = rule.Project }),
        };
        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    private string ProjectRelativeToTsclProject(string workspaceRelativePath)
    {
        if (_workspace.Tscl is null)
        {
            return workspaceRelativePath;
        }

        string projectPath = Path.GetFullPath(_workspace.Tscl.Project!, _workspace.RootDirectory);
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string sourcePath = Path.GetFullPath(workspaceRelativePath, _workspace.RootDirectory);
        return Path.GetRelativePath(projectDirectory, sourcePath).Replace('\\', '/');
    }

    private static string XmlEscape(string value) => SecurityElement.Escape(value) ?? string.Empty;
}
