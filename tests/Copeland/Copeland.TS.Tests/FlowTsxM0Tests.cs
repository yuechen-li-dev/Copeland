using System.Diagnostics;
using System.Text;
using Copeland.TS.Backend.JavaScript;
using Copeland.TS.Compiler;
using Copeland.TS.Mir;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class FlowTsxM0Tests
{
    [Fact]
    public void Tsx_flow_lowers_through_the_existing_flow_mir()
    {
        CopelandCompilation compilation = CompileTsx("""
            enum DoorEvent { Open, Reset, }

            export default (
                <Flow name="Door" events={DoorEvent} board={{ attempts: 0 }}>
                    <State name="Closed" initial>
                        {Open => Opened {
                            board.attempts = board.attempts + 1;
                        }}
                    </State>
                    <State name="Opened">
                        {Reset => Closed}
                    </State>
                </Flow>
            );
            """);

        Assert.True(compilation.Success, Diagnostics(compilation));
        MirFlowDefinition flow = Assert.Single(compilation.MirCompilation!.Program!.Flows);
        Assert.Equal("Door", flow.Name);
        Assert.Equal("Closed", flow.InitialState);
        Assert.Equal("Opened", Assert.Single(flow.States[0].Transitions).TargetState);
        Assert.Equal("attempts", Assert.Single(flow.BoardFields).Name);
        Assert.Contains(
            "TsXmlFlowTransitionArmExpression",
            Copeland.TS.Syntax.SyntaxTreeDumper.Dump(compilation.SyntaxTree!.Root),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Delivery_native_and_tsx_have_the_exact_same_flow_semantic_hash()
    {
        CopelandCompilation native = CompileNative(ReadFixture("Delivery.flow.ts"));
        CopelandCompilation tsx = CompileTsx(ReadFixture("Delivery.flow.tsx"));

        Assert.True(native.Success, Diagnostics(native));
        Assert.True(tsx.Success, Diagnostics(tsx));
        MirFlowDefinition nativeFlow = Assert.Single(native.MirCompilation!.Program!.Flows);
        MirFlowDefinition tsxFlow = Assert.Single(tsx.MirCompilation!.Program!.Flows);
        string nativeHash = MirFlowSemanticHash.Compute(nativeFlow);
        string tsxHash = MirFlowSemanticHash.Compute(tsxFlow);

        Assert.Equal("1e0515b8d0d02c7ce1645a7841443690586a41c6dee3f181d9d05a1a81730fd8", nativeHash);
        Assert.Equal(nativeHash, tsxHash);
    }

    [Fact]
    public async Task Delivery_native_and_tsx_execute_identical_required_paths()
    {
        CopelandCompilation native = CompileNative(ReadFixture("Delivery.flow.ts"));
        CopelandCompilation tsx = CompileTsx(ReadFixture("Delivery.flow.tsx"));
        Assert.True(native.Success, Diagnostics(native));
        Assert.True(tsx.Success, Diagnostics(tsx));

        string nativeTrace = await ExecuteDelivery(JavaScriptBackend.Emit(native.MirCompilation!.Program!).SourceText!);
        string tsxTrace = await ExecuteDelivery(JavaScriptBackend.Emit(tsx.MirCompilation!.Program!).SourceText!);

        Assert.Equal(nativeTrace, tsxTrace);
        Assert.Contains("success|Completed|21|1|true|Completed|21|", nativeTrace, StringComparison.Ordinal);
        Assert.Contains("retry|Completed|22|2|true|Completed|22|", nativeTrace, StringComparison.Ordinal);
        Assert.Contains("reject|Rejected|10|1|false|Failed||delivery rejected", nativeTrace, StringComparison.Ordinal);
        Assert.Contains("cancel|Cancelled|0|0|false|Failed||delivery cancelled", nativeTrace, StringComparison.Ordinal);
        Assert.Contains("reset|Idle|0|0|false|Transitioned||", nativeTrace, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<Unknown />", "COPE-FLOW-TSX-0006")]
    [InlineData("<State name=\"Only\" initial><RuntimeComponent /></State>", "COPE-FLOW-TSX-0006")]
    [InlineData("<State name=\"Only\" initial><On event={Go} to=\"Only\" /></State>", "COPE-FLOW-TSX-0006")]
    [InlineData("<State name=\"Only\" initial>{dynamicChild}</State>", "COPE-FLOW-TSX-0010")]
    [InlineData("<State name={dynamicName} initial />", "COPE-FLOW-TSX-0004")]
    [InlineData("<State name=\"Only\" name=\"Again\" initial />", "COPE-FLOW-TSX-0012")]
    public void Tsx_flow_rejects_invalid_semantic_structure(string child, string diagnosticId)
    {
        string source = $$"""
            enum Event { Go, }
            export default (
                <Flow name="Invalid" events={Event} board={ { value: 0 } }>
                    {{child}}
                </Flow>
            );
            """;

        CopelandCompilation compilation = CompileTsx(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
        Assert.All(compilation.Diagnostics, diagnostic => Assert.True(diagnostic.Length > 0));
    }

    [Theory]
    [InlineData("Missing", "Go", null, "COPE-FLOW-0015")]
    [InlineData("Only", "Missing", null, "COPE-FLOW-0014")]
    [InlineData("Only", "Go(value)", null, "COPE-FLOW-0016")]
    [InlineData("Only", "Go", "1", "COPE-TYPE-0001")]
    public void Tsx_flow_reuses_native_transition_diagnostics(
        string target,
        string eventPattern,
        string? guard,
        string diagnosticId)
    {
        string when = guard is null ? string.Empty : $" when {guard}";
        string source = $$"""
            enum Event { Go, }
            export default (
                <Flow name="Invalid" events={Event} board={ { value: 0 } }>
                    <State name="Only" initial>
                        {{{eventPattern}}{{when}} => {{target}}}
                    </State>
                </Flow>
            );
            """;

        CopelandCompilation compilation = CompileTsx(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Fact]
    public void Tsx_flow_requires_an_explicit_semantic_project_type()
    {
        CopelandCompilation compilation = CopelandCompiler.Compile(
            "enum Event { Go, } export default <Flow name=\"Door\" events={Event} />;",
            new CopelandCompilationOptions
            {
                SourcePath = "Door.flow.tsx",
                TargetStage = CopelandCompilationStage.Bound,
            });

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-FLOW-TSX-0001");
    }

    [Theory]
    [MemberData(nameof(SemanticNegativeCases))]
    public void Tsx_flow_rejects_static_semantic_errors(string source, string diagnosticId)
    {
        CopelandCompilation compilation = CompileTsx(source);

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    public static IEnumerable<object[]> SemanticNegativeCases()
    {
        yield return Case(
            """
            <State name="Only" initial />
            <State name="Only" />
            """,
            "COPE-FLOW-0011");
        yield return Case(
            """
            <State name="First" initial />
            <State name="Second" initial />
            """,
            "COPE-FLOW-0013");
        yield return Case("<State name=\"Only\" initial><Finish value={\"wrong\"} /></State>", "COPE-FLOW-0028", result: "int");
        yield return Case("<State name=\"Only\" initial><Fail error={1} /></State>", "COPE-FLOW-0026", result: "int", failure: "string");
        yield return Case(
            """
            <State name="Only" initial>
                {Go(value) => Only { board.missing = value; }}
            </State>
            """,
            "COPE-FLOW-0022",
            eventDeclaration: "Go(value: int)");
        yield return Case(
            """
            <State name="Only" initial>
                {Go(value) => Only { board.value = value; }}
            </State>
            """,
            "COPE-FLOW-0023",
            eventDeclaration: "Go(value: string)");
        yield return Case(
            """
            <State name="Only" initial>
                {Go => Only}
                <Finish />
            </State>
            """,
            "COPE-FLOW-0031");
    }

    private static object[] Case(
        string states,
        string diagnosticId,
        string? result = null,
        string? failure = null,
        string eventDeclaration = "Go")
    {
        string resultAttribute = result is null ? string.Empty : $" result=\"{result}\"";
        string failureAttribute = failure is null ? string.Empty : $" failure=\"{failure}\"";
        string source = $$"""
            enum Event { {{eventDeclaration}}, }
            export default (
                <Flow name="Invalid" events={Event}{{resultAttribute}}{{failureAttribute}} board={ { value: 0 } }>
                    {{states}}
                </Flow>
            );
            """;
        return [source, diagnosticId];
    }

    [Fact]
    public void Tsx_flow_has_no_react_or_jsx_runtime_dependency()
    {
        string source = ReadFixture("Delivery.flow.tsx");
        CopelandCompilation compilation = CompileTsx(source);
        Assert.True(compilation.Success, Diagnostics(compilation));

        string emitted = JavaScriptBackend.Emit(compilation.MirCompilation!.Program!).SourceText!;
        Assert.DoesNotContain("React", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("jsx", emitted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createElement", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("Flow(", emitted, StringComparison.Ordinal);
    }

    private static CopelandCompilation CompileTsx(string source)
        => CopelandCompiler.Compile(
            source,
            new CopelandCompilationOptions
            {
                SourcePath = "Door.flow.tsx",
                ProjectTypes = CopelandProjectTypeSet.FlowAuthoring,
            });

    private static CopelandCompilation CompileNative(string source)
        => CopelandCompiler.Compile(
            source,
            new CopelandCompilationOptions
            {
                SourcePath = "Delivery.flow.ts",
            });

    private static string ReadFixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "FlowTsxM0", name));

    private static async Task<string> ExecuteDelivery(string javaScript)
    {
        const string script = """
            function trace(label, actions) {
                const session = Delivery.start();
                let outcome = null;
                for (const action of actions) outcome = action(session);
                console.log([
                    label,
                    session.state,
                    session.board.total,
                    session.board.attempts,
                    session.board.accepted,
                    outcome.kind,
                    outcome.value ?? "",
                    outcome.error ?? ""
                ].join("|"));
            }
            trace("success", [s => s.sendStart(10), s => s.sendTick(5), s => s.sendAccept(2), s => s.sendTick(1), s => s.sendAccept(3)]);
            trace("retry", [s => s.sendStart(10), s => s.sendRetry(5), s => s.sendTick(2), s => s.sendAccept(0), s => s.sendTick(1), s => s.sendAccept(4)]);
            trace("reject", [s => s.sendStart(10), s => s.sendReject(42)]);
            trace("cancel", [s => s.sendCancel()]);
            trace("reset", [s => s.sendStart(10), s => s.sendAccept(1), s => s.sendReset()]);
            """;
        string path = Path.Combine(Path.GetTempPath(), "copeland-flow-tsx-" + Guid.NewGuid().ToString("N") + ".js");
        try
        {
            await File.WriteAllTextAsync(path, javaScript + script, new UTF8Encoding(false));
            using Process process = Process.Start(new ProcessStartInfo("node", '"' + path + '"')
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(string.Empty, stderr);
            return stdout.Replace("\r\n", "\n", StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string Diagnostics(CopelandCompilation compilation)
        => string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.ToString()));
}
