using Copeland.TS.Backend.AspNetCore;
using Copeland.TS.Backend.CSharp;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class AspNetBridgeM0Tests
{
    [Fact]
    public void Remote_declaration_generates_one_versioned_contract_and_fixed_route()
    {
        CopelandProjectCompilation compilation = CompileBridge();

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        MirProjectGraph graph = compilation.MirProjectGraph!;
        MirFunction remote = Assert.Single(graph.AggregateProgram.Functions, function => function.IsRemote);
        CopelandBridgeGeneration bridge = CopelandBridgeGenerator.Generate(graph);

        Assert.Contains(@"""schemaVersion"": 1", bridge.ContractJson, StringComparison.Ordinal);
        Assert.Contains(@"""id"": ""Bridge.ts/SerializeState""", bridge.ContractJson, StringComparison.Ordinal);
        Assert.Contains(@"""route"": ""/__copeland/m0/bridge/serialize-state""", bridge.ContractJson, StringComparison.Ordinal);
        Assert.Equal("/__copeland/m0/bridge/serialize-state", bridge.Routes[remote.Name]);
        Assert.Contains(@"MapPost(""/__copeland/m0/bridge/serialize-state""", bridge.EndpointSource, StringComparison.Ordinal);
        Assert.Contains("CopelandModule.SerializeState(request)", bridge.EndpointSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_declaration_emits_typed_browser_fetch_and_direct_CLR_SystemTextJson()
    {
        CopelandProjectCompilation compilation = CompileBridge();

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        MirProjectGraph graph = compilation.MirProjectGraph!;
        CopelandBridgeGeneration bridge = CopelandBridgeGenerator.Generate(graph);
        JavaScriptProjectCompilation browser = JavaScriptProjectEmitter.Emit(
            graph,
            new JavaScriptEmissionOptions
            {
                Profile = JavaScriptEmissionProfile.Production,
                RuntimeTarget = JavaScriptRuntimeTarget.Browser,
                RemoteOperationRoutes = bridge.Routes,
            });
        CSharpCompilation clr = CSharpBackend.Emit(graph.AggregateProgram);

        Assert.True(browser.Success, string.Join(Environment.NewLine, browser.Diagnostics));
        string browserSource = browser.Files["Bridge.js"];
        Assert.Contains("globalThis.fetch", browserSource, StringComparison.Ordinal);
        Assert.Contains(@"JSON.stringify({ ""message"": request.$f0, ""count"": request.$f1 })", browserSource, StringComparison.Ordinal);
        Assert.Contains("export {", browserSource, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", browserSource, StringComparison.Ordinal);
        Assert.Contains("global::System.Text.Json.JsonSerializer.Serialize", clr.SourceText, StringComparison.Ordinal);
        Assert.Contains(@"JsonPropertyName(""message"")", clr.SourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("GetMethod", clr.SourceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Bridge_generator_rejects_an_unsupported_boundary_record()
    {
        CopelandProjectCompilation compilation = CopelandProjectCompiler.CompileToMir(
            [
                new CopelandProjectSource(
                    "Bridge.ts",
                    "Bridge.ts",
                    """
                    export record Request { values: number; }
                    export record BridgeError { kind: string; message: string; }
                    export remote function SerializeState(request: Request): string ! BridgeError {
                        return "not a CLR proof";
                    }
                    """),
            ]);

        Assert.True(compilation.Success, string.Join(Environment.NewLine, compilation.Diagnostics));
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CopelandBridgeGenerator.Generate(compilation.MirProjectGraph!));
        Assert.Contains("supports only string, int, and bool", exception.Message, StringComparison.Ordinal);
    }

    private static CopelandProjectCompilation CompileBridge()
        => CopelandProjectCompiler.CompileToMir(
            [
                new CopelandProjectSource(
                    "Bridge.ts",
                    "Bridge.ts",
                    """
                    using System.Text.Json;
                    export record SerializeRequest {
                        message: string;
                        count: int;
                    }
                    export record BridgeError {
                        kind: string;
                        message: string;
                    }
                    export remote function SerializeState(request: SerializeRequest): string ! BridgeError {
                        return JsonSerializer.Serialize(request);
                    }
                    """),
            ]);
}
