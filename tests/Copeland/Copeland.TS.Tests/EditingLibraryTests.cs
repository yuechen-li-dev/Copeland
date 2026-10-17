using System.Text.Json;
using System.Text.Encodings.Web;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.InstinctCorpus;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class EditingLibraryTests
{
    [Fact]
    public void Msbuild_library_is_consumable_as_an_ordinary_typed_dotnet_reference()
    {
        var parsed = Libraries.Copeland.Editing.parseCentsText("-21474836.48");
        Assert.True(parsed.IsOk);
        Assert.Equal(int.MinValue, parsed.Value);
        Assert.Equal("-21474836.48", Libraries.Copeland.Editing.formatCents(parsed.Value));
        var incomplete = Libraries.Copeland.Editing.parseCentsText("1.");
        Assert.False(incomplete.IsOk);
        Assert.IsType<Libraries.Copeland.ParseError.Incomplete>(incomplete.Error);
        var precision = Libraries.Copeland.Editing.parseCentsText("1.234");
        Assert.Equal(2, Assert.IsType<Libraries.Copeland.ParseError.FractionTooLong>(precision.Error).places);
    }

    [Fact]
    public async Task Ordinary_editing_library_preserves_drafts_and_exact_numeric_policy_on_both_backends()
    {
        string root = FindRepository();
        string library = Path.Combine(root, "libraries/Copeland.Editing/Editing.ts");
        string[] integerInputs = ["", "-", "+", "0", "-0", "+0012", "-1", "2147483647", "-2147483648", "2147483648", "-2147483649", "1.5", "1e2", " 1", "1 ", "１２", "--1"];
        string[] integerExpected = ["empty", "incomplete", "incomplete", "0", "0", "12", "-1", "2147483647", "-2147483648", "range", "range", "character:1", "character:1", "character:0", "character:1", "character:0", "character:1"];
        string[] centsInputs = ["", "-", "+", "1.", "1", "1.2", "1.23", "1.234", ".50", "-0.01", "+0001.20", "21474836.47", "-21474836.48", "21474836.48", "-21474836.49", "21474837", "1e2", "1,00", "1.2.3"];
        string[] centsExpected = ["empty", "incomplete", "incomplete", "incomplete", "100", "120", "123", "precision:2", "character:0", "-1", "120", "2147483647", "-2147483648", "range", "range", "range", "character:1", "character:1", "character:3"];
        var sourceJson = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        string calls = string.Join(",\n", integerInputs.Select(text => $"inspect(parseIntText({JsonSerializer.Serialize(text, sourceJson)}))")
            .Concat(centsInputs.Select(text => $"inspect(parseCentsText({JsonSerializer.Serialize(text, sourceJson)}))")));
        string probe = """
            import { Draft, ParseError, beginDraft, editDraft, revealDraft, cancelDraft, isDirty,
                parseIntText, parseCentsText, formatInt, formatCents } from "./Editing";
            import { Holder, makeHolder } from "./Holder";
            function inspect(value: int ! ParseError): string {
                return match value {
                    ok(parsed) => String.From(parsed),
                    err(error) => match error {
                        Empty => "empty",
                        Incomplete => "incomplete",
                        InvalidCharacter(position) => "character:" + String.From(position),
                        FractionTooLong(places) => "precision:" + String.From(places),
                        OutOfRange => "range",
                    },
                };
            }
            export function run(): string[] {
                return [
            """ + calls + """
                ];
            }
            export function drafts(): string[] {
                const holder: Holder = makeHolder();
                const initial: Draft<int, ParseError> = holder.draft;
                const incomplete: Draft<int, ParseError> = editDraft<int, ParseError>(initial, "-", parseIntText);
                const revealed: Draft<int, ParseError> = revealDraft<int, ParseError>(incomplete);
                const corrected: Draft<int, ParseError> = editDraft<int, ParseError>(revealed, "12", parseIntText);
                const cancelled: Draft<int, ParseError> = cancelDraft<int, ParseError>(corrected);
                const accepted: Draft<int, ParseError> = beginDraft<int, ParseError>(12, formatInt);
                return [incomplete.text, inspect(incomplete.value), String.From(incomplete.accepted),
                    String.From(incomplete.revealErrors), String.From(isDirty<int, ParseError>(incomplete)),
                    inspect(corrected.value), String.From(corrected.revealErrors), cancelled.text,
                    String.From(cancelled.revealErrors), String.From(isDirty<int, ParseError>(cancelled)),
                    accepted.text, formatCents(0), formatCents(1), formatCents(-1),
                    formatCents(2147483647), formatCents(-2147483648)];
            }
            """;
        CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir([
            new("Editing.ts", library, File.ReadAllText(library)),
            new("Holder.ts", "Holder.ts", """
                import { Draft, ParseError, beginDraft, formatInt } from "./Editing";
                export record Holder { draft: Draft<int, ParseError>; }
                export function makeHolder(): Holder {
                    return { draft: beginDraft<int, ParseError>(4, formatInt) };
                }
                """),
            new("Probe.ts", "Probe.ts", probe),
        ]);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        var csharp = CSharpBackend.Emit(compilation.MirProjectGraph!.AggregateProgram);
        Assert.Empty(csharp.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(csharp.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        string[] expected = integerExpected.Concat(centsExpected).ToArray();
        string[] draftExpected = ["-", "incomplete", "4", "false", "true", "12", "true", "4", "false", "false", "12", "0.00", "0.01", "-0.01", "21474836.47", "-21474836.48"];
        Assert.Equal(expected, (string[])module.GetMethod("run")!.Invoke(null, null)!);
        Assert.Equal(draftExpected, (string[])module.GetMethod("drafts")!.Invoke(null, null)!);
        string directory = Path.Combine(Path.GetTempPath(), "copeland-editing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "package.json"), "{\"type\":\"module\"}");
            foreach (JavaScriptEmissionProfile profile in Enum.GetValues<JavaScriptEmissionProfile>())
            {
                var emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph, new JavaScriptEmissionOptions { Profile = profile });
                Assert.Empty(emitted.Diagnostics);
                foreach ((string name, string content) in emitted.Files)
                {
                    string path = Path.Combine(directory, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, content);
                }
                string entry = JsonSerializer.Serialize(new Uri(Path.Combine(directory, "Probe.js")).AbsoluteUri);
                string actual = await CorpusRunner.RunNode($"import {{ run, drafts }} from {entry};\nconsole.log(JSON.stringify([run(), drafts()]));");
                Assert.Equal(JsonSerializer.Serialize(new[] { expected, draftExpected }), actual.Trim());
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Unrelated_generic_definitions_remain_nominal_across_modules()
    {
        CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir([
            new("A.ts", "A.ts", "export record Box<T> { value: T; } export function first(): Box<int> { return { value: 1 }; }"),
            new("B.ts", "B.ts", "export record Box<T> { value: T; }"),
            new("Use.ts", "Use.ts", "import { first } from './A'; import { Box } from './B'; export function bad(): Box<int> { return first(); }"),
        ]);
        Assert.False(compilation.Success);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-REC-0015");
    }

    [Fact]
    public async Task Cafe_editing_reducer_uses_the_same_typed_application_on_csharp_and_javascript()
    {
        string root = FindRepository();
        CopelandProjectSource Source(string logical, string relative)
        {
            string path = Path.Combine(root, relative);
            return new(logical, path, File.ReadAllText(path));
        }
        CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir([
            Source("Workbook.ts", "samples/copeland-ts/tablescript-friction-m12/Workbook/Workbook.ts"),
            Source("Application.ts", "samples/copeland-ts/tablescript-application-m13/src/Application.ts"),
            Source("PriceApplication.ts", "samples/copeland-ts/tablescript-editing-m14/src/PriceApplication.ts"),
            Source("Editing.ts", "libraries/Copeland.Editing/Editing.ts"),
            Source("Editor.ts", "samples/copeland-ts/tablescript-editing-m14/src/Editor.ts"),
        ]);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        var csharp = CSharpBackend.Emit(compilation.MirProjectGraph!.AggregateProgram);
        Assert.Empty(csharp.Diagnostics);
        Type module = CorpusRunner.CompileCSharp(csharp.SourceText).GetType("Copeland.Generated.CopelandModule", true)!;
        int[] expected = [31210, 31210, 12710, 2, 310, 15750, 15750, 1, 500, 15750, 1, 1, 31210];
        Assert.Equal(expected, (int[])module.GetMethod("editingProbe")!.Invoke(null, null)!);
        string directory = Path.Combine(Path.GetTempPath(), "copeland-cafe-editing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "package.json"), "{\"type\":\"module\"}");
            foreach (JavaScriptEmissionProfile profile in Enum.GetValues<JavaScriptEmissionProfile>())
            {
                var emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph, new JavaScriptEmissionOptions { Profile = profile });
                Assert.Empty(emitted.Diagnostics);
                foreach ((string name, string content) in emitted.Files)
                {
                    string path = Path.Combine(directory, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, content);
                }
                string entry = JsonSerializer.Serialize(new Uri(Path.Combine(directory, "Editor.js")).AbsoluteUri);
                string actual = await CorpusRunner.RunNode($"import {{ editingProbe }} from {entry};\nconsole.log(JSON.stringify(editingProbe()));");
                Assert.Equal(JsonSerializer.Serialize(expected), actual.Trim());
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string FindRepository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Cannot find repository sources.");
    }
}
