using System.Diagnostics;
using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Backend.JavaScript.Tests;

public sealed class PresentationHostTests
{
    [Theory]
    [InlineData(false, JavaScriptEmissionProfile.Diagnostic)]
    [InlineData(false, JavaScriptEmissionProfile.Symbolic)]
    [InlineData(false, JavaScriptEmissionProfile.Production)]
    [InlineData(true, JavaScriptEmissionProfile.Diagnostic)]
    [InlineData(true, JavaScriptEmissionProfile.Symbolic)]
    [InlineData(true, JavaScriptEmissionProfile.Production)]
    public async Task Contracted_hosts_keep_match_arm_property_setup_in_every_profile(bool vue, JavaScriptEmissionProfile profile)
    {
        const string source = """
            import { Control } from "controls";
            enum Kind { First, Second }
            record Data { value: string; }
            function Select(kind: Kind, data: Data): View {
                return match kind {
                    First => <Control hostKey="first" value={data.value} />,
                    Second => <Control hostKey="second" value={data.value} />,
                };
            }
            export function First(): View { return Select(Kind.First, { value: "first value" }); }
            export function Second(): View { return Select(Kind.Second, { value: "second value" }); }
            """;
        var compilation = Compile(source, vue,
            [new("hostKey", "string", true), new("value", "string", true)]);
        Assert.True(compilation.Success, string.Join("\n", compilation.Diagnostics));
        Assert.Contains(compilation.MirProjectGraph!.Modules.SelectMany(module => module.NpmImports), import => import.PresentationHost);
        var emitted = JavaScriptProjectEmitter.Emit(compilation.MirProjectGraph,
            new() { RuntimeTarget = JavaScriptRuntimeTarget.Browser, Profile = profile });
        Assert.True(emitted.Success);
        string script = string.Join("\n", emitted.Files["Page.js"].Split('\n')
            .Where(line => !line.StartsWith("import ", StringComparison.Ordinal)));
        script = "const Control = {}; const __cope_view_element = (type, props) => ({ type, props });\n"
            + script + "\nconsole.log(First().props.value + '|' + Second().props.value);";
        Assert.Equal("first value|second value", await Execute(script));
    }

    [Theory]
    [InlineData("View")]
    [InlineData("View[]")]
    [InlineData("ReactNode")]
    [InlineData("VueNode")]
    [InlineData("()=>ReactNode")]
    [InlineData("(string)=>string")]
    [InlineData("(()=>void)=>void")]
    public void Foreign_host_props_cannot_smuggle_renderer_values(string type)
    {
        var compilation = Compile("import { Control } from \"controls\"; export function Page(): View { return <Control hostKey=\"x\" />; }",
            false, [new("hostKey", "string", true), new("opaque", type)]);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-VIEW-HOST-0001");
    }

    [Fact]
    public void Foreign_host_requires_a_domain_identity()
    {
        var compilation = Compile("import { Control } from \"controls\"; export function Page(): View { return <Control />; }", false, []);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-VIEW-HOST-0001");
    }

    private static CopelandProjectCompilation Compile(string source, bool vue, IReadOnlyList<CopelandNpmComponentPropertyContract> properties)
    {
        var adapter = vue ? MirPresentationAdapter.Vue : MirPresentationAdapter.React;
        return CopelandProjectCompiler.CompileToMir([new("Page.tsx", "Page.tsx", source)], new()
        {
            ProjectTypes = CopelandProjectTypeSet.CopelandViews,
            PresentationAdapter = adapter,
            NpmDependencies = new([
                new(adapter.ElementModule, "test", [new(adapter.ElementExport, [], "View")]),
                new("controls", "test", [], Components: [new("Control", properties,
                    Renderer: adapter.Identity, PresentationHost: true)]),
            ]),
        });
    }

    private static async Task<string> Execute(string script)
    {
        string directory = Path.Combine(Path.GetTempPath(), "copeland-presentation-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "probe.mjs");
            await File.WriteAllTextAsync(path, script);
            var start = new ProcessStartInfo("node")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
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
                throw new TimeoutException("Generated presentation host probe timed out.");
            }
            Assert.True(process.ExitCode == 0, await errors);
            return (await output).Trim();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
