using System.Diagnostics;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class ContributionStreamTests
{
    private const string Layouts = """
        layout Wide<0px, 0px> {
            width: fill; height: fit;
            row root {
                slot list { width: 300px; height: fit; }
                slot detail { width: fill; height: fit; }
            }
        }
        layout Stacked<0px, 0px> {
            width: fill; height: fit;
            column root {
                slot list { height: fit; }
                slot detail { height: fit; }
            }
        }
        """;

    [Theory]
    [InlineData(JavaScriptEmissionProfile.Diagnostic)]
    [InlineData(JavaScriptEmissionProfile.Symbolic)]
    [InlineData(JavaScriptEmissionProfile.Production)]
    public async Task Local_channels_construct_once_in_authored_order_and_bind_without_reconstruction(JavaScriptEmissionProfile profile)
    {
        CopelandProjectCompilation compilation = Compile(Layouts + """
            function Leaf(text: string): ReactNode { return <span>{text}</span>; }
            export function Main(stacked: boolean): ReactNode {
                stream Content { list: Leaf("list"); detail: Leaf("detail"); }
                const wide = bind Wide using Content;
                const compact = bind Stacked using Content;
                if (stacked) { return compact; }
                return wide;
            }
            """);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(2, Assert.Single(compilation.Modules).BoundCompilation!.Program.LayoutBindings.Count);
        JavaScriptCompilation emission = JavaScriptBackend.Emit(compilation.MirProjectGraph!.AggregateProgram,
            new JavaScriptEmissionOptions { Profile = profile });
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        string script = RemoveReactImport(emission.SourceText!) + """
            const wide = Main(false);
            const compact = Main(true);
            console.log(JSON.stringify(calls));
            console.log(wide.children.length + ":" + compact.children.length);
            """;
        string output = await RunNode(script);
        Assert.Equal("[\"list\",\"detail\",\"list\",\"detail\"]\n2:2", output);
    }

    [Fact]
    public async Task Module_and_class_streams_initialize_once_and_local_streams_initialize_per_invocation()
    {
        CopelandProjectCompilation compilation = Compile(Layouts + """
            function Leaf(text: string): ReactNode { return <span>{text}</span>; }
            stream Shared { list: Leaf("module"); detail: Leaf("module detail"); }
            class Palette {
                count: int;
                constructor(): Palette { return { count: 0 }; }
                stream Content { list: Leaf("class"); detail: Leaf("class detail"); }
                View(): ReactNode { return bind Wide using Palette.Content; }
            }
            export function Main(): ReactNode {
                const first = bind Wide using Shared;
                const second = bind Stacked using Shared;
                const third = Palette.View();
                stream Content { list: Leaf("local"); detail: Leaf("local detail"); }
                return bind Wide using Content;
            }
            """);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(2, compilation.MirProjectGraph!.AggregateProgram.ModuleValues.Count);
        JavaScriptCompilation emission = JavaScriptBackend.Emit(compilation.MirProjectGraph.AggregateProgram);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        string output = await RunNode(RemoveReactImport(emission.SourceText!) + "Main(); Main(); console.log(JSON.stringify(calls));");
        Assert.Equal("[\"module\",\"module detail\",\"class\",\"class detail\",\"local\",\"local detail\",\"local\",\"local detail\"]", output);
    }

    [Fact]
    public async Task Project_import_aliases_read_the_origin_owned_module_stream_without_duplicating_initialization()
    {
        CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir(
        [
            new CopelandProjectSource("Content.tsx", "Content.tsx", """
                import { createElement } from "react";
                export stream Shared { list: <span>List</span>; detail: <span>Detail</span>; }
                """),
            new CopelandProjectSource("Main.tsx", "Main.tsx", """
                import { createElement } from "react";
                import { Shared as Content } from "./Content";
                """ + Layouts + """
                export function Main(): ReactNode { return bind Wide using Content; }
                """)
        ], Options());
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptProjectCompilation emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph!);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        string storage = Assert.Single(compilation.MirProjectGraph!.AggregateProgram.ModuleValues).Name;
        Assert.Contains("const " + storage + " = ", emitted.Files["Content.js"], StringComparison.Ordinal);
        Assert.DoesNotContain("const " + storage + " = ", emitted.Files["Main.js"], StringComparison.Ordinal);
        Assert.Contains("import { Shared as ", emitted.Files["Main.js"], StringComparison.Ordinal);
        var files = new Dictionary<string, string>(emitted.Files)
        {
            ["package.json"] = "{\"type\":\"module\"}",
            ["node_modules/react/package.json"] = "{\"type\":\"module\",\"exports\":\"./index.js\"}",
            ["node_modules/react/index.js"] = """
                export function createElement(type, props, ...children) {
                    globalThis.calls ??= [];
                    if (type === "span") globalThis.calls.push(children[0]);
                    return { type, props, children };
                }
                """,
        };
        string output = await RunNode("import { Main } from './Main.js'; Main(); Main(); console.log(JSON.stringify(globalThis.calls));", files);
        Assert.Equal("[\"List\",\"Detail\"]", output);
    }

    [Fact]
    public void Module_stream_initializers_reject_transitive_reads_of_later_module_values()
    {
        CopelandProjectCompilation compilation = Compile("""
            function ReadLater(): ReactNode { return Later.item; }
            stream First { item: ReadLater(); }
            stream Later { item: <span>Later</span>; }
            """);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-STREAM-CONTENT-0007"
            && diagnostic.Message.Contains("before initialization", StringComparison.Ordinal));
    }

    [Fact]
    public void Private_class_streams_retain_class_access_control()
    {
        CopelandProjectCompilation compilation = Compile(Layouts + """
            class Palette {
                count: int;
                constructor(): Palette { return { count: 0 }; }
                private stream Content { list: <span>List</span>; detail: <span>Detail</span>; }
            }
            export function Main(): ReactNode { return bind Wide using Palette.Content; }
            """);
        Assert.Equal("COPE-CLASS-0009", Assert.Single(compilation.Diagnostics).Id);
    }

    [Theory]
    [InlineData(JavaScriptEmissionProfile.Diagnostic)]
    [InlineData(JavaScriptEmissionProfile.Symbolic)]
    [InlineData(JavaScriptEmissionProfile.Production)]
    public async Task Named_capture_initializers_use_outer_scope_and_ordered_once_only_snapshots(JavaScriptEmissionProfile profile)
    {
        CopelandProjectCompilation compilation = Compile("""
            export function main(): number {
                let current: number = 1;
                const renamed: number = 7;
                const value = capture {
                    renamed: current = current + 1,
                    second: current = current + 1,
                    outer: renamed
                } () => renamed * 100 + second * 10 + outer;
                current = 99;
                return value() + value();
            }
            """);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptCompilation emitted = JavaScriptBackend.Emit(compilation.MirProjectGraph!.AggregateProgram,
            new JavaScriptEmissionOptions { Profile = profile });
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.Equal("474", await RunNode(RemoveReactImport(emitted.SourceText!) + "console.log(main());"));
    }

    [Theory]
    [InlineData("stream Content { list: <span>First</span>; list: <span>Second</span>; }", "COPE-STREAM-CONTENT-0002")]
    [InlineData("stream Content { list: 1; detail: <span>Detail</span>; }", "COPE-STREAM-CONTENT-0003")]
    [InlineData("stream Content { list: <span>List</span>; }", "COPE-STREAM-CONTENT-0006")]
    [InlineData("stream Content { list: <span>List</span>; detail: <span>Detail</span>; extra: <span>Extra</span>; }", "COPE-STREAM-CONTENT-0005")]
    [InlineData("const Content = 1;", "COPE-STREAM-CONTENT-0004")]
    public void Invalid_channels_have_named_diagnostics(string declaration, string expected)
    {
        CopelandProjectCompilation compilation = Compile(Layouts + $$"""
            export function Main(): ReactNode {
                {{declaration}}
                return bind Wide using Content;
            }
            """);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == expected);
    }

    [Fact]
    public void Contribution_layouts_use_existing_spacing_and_fixed_track_css()
    {
        CopelandProjectCompilation compilation = Compile(Layouts + """
            export function Main(): ReactNode {
                stream Content { list: <span>List</span>; detail: <span>Detail</span>; }
                return bind Wide using Content;
            }
            """);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptProjectCompilation emitted = LayoutJavaScriptProjectEmitter.AddLayouts(
            JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph!), compilation.Modules);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.Contains("flex: 0 0 300px;", emitted.Files["generated/layouts.css"], StringComparison.Ordinal);
    }

    [Fact]
    public void Matching_channels_do_not_bypass_an_explicit_layout_topology_contract()
    {
        CopelandProjectCompilation compilation = Compile("""
            layout type Shell { row root { slot body; } }
            layout Invalid<0px, 0px> satisfies Shell {
                width: fill; height: fit;
                column root { slot body { height: fit; } }
            }
            stream Content { body: <span>Body</span>; }
            export function Main(): ReactNode { return bind Invalid using Content; }
            """);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id.StartsWith("COPE-LAYOUT-TYPE-", StringComparison.Ordinal));
        Assert.Null(compilation.MirProjectGraph);
    }

    [Fact]
    public void Fixed_layout_binding_reuses_the_existing_projection_dimensions_and_origin()
    {
        CopelandProjectCompilation compilation = Compile("""
            layout Offset<24px, 12px> {
                width: 640px; height: 480px;
                column root { slot body { height: fill; } }
            }
            stream Content { body: <span>Body</span>; }
            export function Main(): ReactNode { return bind Offset using Content; }
            """);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        JavaScriptProjectCompilation emitted = LayoutJavaScriptProjectEmitter.AddLayouts(
            JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph!), compilation.Modules);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        string css = emitted.Files["generated/layouts.css"];
        Assert.Contains("width: 640px;", css, StringComparison.Ordinal);
        Assert.Contains("height: 480px;", css, StringComparison.Ordinal);
        Assert.Contains("left: 24px;", css, StringComparison.Ordinal);
        Assert.Contains("top: 12px;", css, StringComparison.Ordinal);
        Assert.DoesNotContain("m-component-", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Contribution_channels_can_use_layout_property_or_container_words()
    {
        CopelandProjectCompilation compilation = Compile("""
            layout Page<0px, 0px> {
                width: fill; height: fit;
                column root { slot width { height: fit; } slot column { height: fit; } }
            }
            stream Content { width: <span>Width</span>; column: <span>Column</span>; }
            export function Main(): ReactNode { return bind Page using Content; }
            """);
        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    private static CopelandProjectCompilation Compile(string source)
        => CopelandProjectCompiler.CompileToMir(
            [new CopelandProjectSource("Main.tsx", "Main.tsx", "import { createElement } from \"react\";\n" + source)], Options());

    private static CopelandCompilationOptions Options() => new()
    {
        TsXmlProfile = CopelandTsXmlProfile.ReactM0,
        NpmPackages = [new CopelandNpmPackageContract("react", "19.2.7", [new CopelandNpmFunctionContract("createElement", [], "ReactNode")])],
    };

    // This small host measures generated evaluation counts. Real React/DOM
    // interaction is qualified separately by the friction browser corpus.
    private static string RemoveReactImport(string source)
    {
        string code = string.Join("\n", source.Split('\n').Where(line => !line.StartsWith("import ", StringComparison.Ordinal)));
        return """
            const calls = [];
            function createElement(type, props, ...children) {
                if (type === "span") calls.push(children[0]);
                return { type, props, children };
            }
            """ + "\n" + code;
    }

    private static async Task<string> RunNode(string source, IReadOnlyDictionary<string, string>? files = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), "copeland-stream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            if (files is not null)
            {
                foreach ((string fileName, string text) in files)
                {
                    string target = Path.GetFullPath(Path.Combine(directory, fileName));
                    Assert.StartsWith(directory + Path.DirectorySeparatorChar, target, StringComparison.OrdinalIgnoreCase);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await File.WriteAllTextAsync(target, text);
                }
            }
            string path = Path.Combine(directory, "probe.mjs");
            await File.WriteAllTextAsync(path, source);
            var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add(path);
            using Process process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("The generated stream probe did not complete within 30 seconds.");
            }
            Assert.True(process.ExitCode == 0, await errors);
            return (await output).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
