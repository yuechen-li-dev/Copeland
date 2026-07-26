using Copeland.TS.Lowering;
using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class FlowM1Tests
{
    [Fact]
    public void Flow_declaration_binds_to_a_dedicated_mir_graph()
    {
        const string source = """
            flow Door {
                board { attempts: number = 0; }
                event Open();
                state Closed initial {
                    on Open() -> Opened { board.attempts = board.attempts + 1; };
                }
                state Opened { }
            }
            """;

        var tree = SyntaxTree.Parse(source);

        Assert.Empty(tree.Diagnostics);
        var compilation = MirLowerer.Lower(tree);
        Assert.Empty(compilation.Diagnostics);
        var flow = Assert.Single(compilation.Program!.Flows);
        Assert.Equal("Door", flow.Name);
        Assert.Equal("Closed", flow.InitialState);
        Assert.Equal("Opened", Assert.Single(flow.States.Single(state => state.Name == "Closed").Transitions).TargetState);
    }

    [Fact]
    public void Flow_reports_missing_initial_unknown_target_and_ambiguous_event_declarations()
    {
        var compilation = MirLowerer.Lower(SyntaxTree.Parse("""
            flow Invalid {
                board { value: number = 0; }
                event Go();
                state First { on Go() -> Missing; on Go() -> First; }
            }
            """));

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-FLOW-0012");
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-FLOW-0015");
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-FLOW-0019");
    }

    [Theory]
    [InlineData("flow Complete -> number { board { value: number = 1; } state Done initial { finish board.value; } }")]
    [InlineData("flow Failed -> number ! string { board { value: number = 1; } state Done initial { fail \"bad\"; } }")]
    [InlineData("flow Empty -> void { board { value: number = 1; } state Done initial { finish; } }")]
    public void Flow_accepts_declared_terminal_contracts(string source)
    {
        var compilation = MirLowerer.Lower(SyntaxTree.Parse(source));

        Assert.Empty(compilation.Diagnostics);
    }

    [Theory]
    [InlineData("flow Bad -> number { board { value: number = 1; } state Done initial { finish \"wrong\"; } }", "COPE-FLOW-0028")]
    [InlineData("flow Bad -> number { board { value: number = 1; } state Done initial { finish; } }", "COPE-FLOW-0029")]
    [InlineData("flow Bad -> number ! string { board { value: number = 1; } state Done initial { fail 1; } }", "COPE-FLOW-0026")]
    [InlineData("flow Bad -> number ! string { board { value: number = 1; } state Done initial { fail; } }", "COPE-FLOW-0030")]
    public void Flow_reports_terminal_contract_mismatches(string source, string diagnosticId)
    {
        var compilation = MirLowerer.Lower(SyntaxTree.Parse(source));

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }
}
