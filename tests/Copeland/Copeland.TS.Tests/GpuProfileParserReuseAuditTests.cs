using Copeland.TS.Syntax;
using Xunit;

namespace Copeland.TS.Tests;

public sealed class GpuProfileParserReuseAuditTests
{
    [Fact]
    public void Vts_Uses_The_Ordinary_Module_Parser_For_Shader_Shaped_Source()
    {
        const string source = """
            import { Dot } from "@sdsl-v/core";

            interface NumericElement {
                zero: f32;
            }

            record ComputeIo {
                input: StorageBuffer<f32>;
                output: StorageBuffer<f32>;
            }

            enum WriteDecision {
                Skip,
                Write(value: f32),
            }

            function AddOne<T extends NumericElement>(value: T): T {
                return value + 1.0;
            }

            function ComputeNoRegression(thread: uint3, io: ComputeIo): void {
                const index: u32 = thread.x;
                if (index < io.output.length) {
                    io.output[index] = io.input[index] + 1.0;
                }
            }
            """;

        Assert.Equal(SourceFileKind.TypeScriptModule, SourceFileKindExtensions.FromSourcePath("compute.v.ts"));

        SyntaxTree tree = SyntaxTree.Parse(source, "compute.v.ts");

        Assert.Empty(tree.Diagnostics);
        Assert.Contains(tree.Root.Members, member => member is ImportDeclarationSyntax);
        Assert.Contains(tree.Root.Members, member => member is InterfaceDeclarationSyntax);
        Assert.Contains(tree.Root.Members, member => member is RecordDeclarationSyntax);
        Assert.Contains(tree.Root.Members, member => member is EnumDeclarationSyntax);
        Assert.Equal(2, tree.Root.Members.OfType<FunctionDeclarationSyntax>().Count());

        FunctionDeclarationSyntax generic = tree.Root.Members
            .OfType<FunctionDeclarationSyntax>()
            .Single(function => function.Identifier.Text == "AddOne");
        Assert.Single(generic.TypeParameters);
        Assert.Equal("NumericElement", Assert.Single(generic.TypeParameters[0].RequirementNames).Text);

        FunctionDeclarationSyntax entry = tree.Root.Members
            .OfType<FunctionDeclarationSyntax>()
            .Single(function => function.Identifier.Text == "ComputeNoRegression");
        IfStatementSyntax conditional = Assert.IsType<IfStatementSyntax>(entry.Body.Statements[1]);
        BlockStatementSyntax thenBlock = Assert.IsType<BlockStatementSyntax>(conditional.ThenStatement);
        ExpressionStatementSyntax assignmentStatement = Assert.IsType<ExpressionStatementSyntax>(thenBlock.Statements[0]);
        AssignmentExpressionSyntax assignment = Assert.IsType<AssignmentExpressionSyntax>(assignmentStatement.Expression);
        Assert.IsType<IndexExpressionSyntax>(assignment.Left);
    }
}
