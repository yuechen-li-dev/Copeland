using Copeland.Script.Codegen.CSharp;
using Copeland.Script.Diagnostics;
using Copeland.Script.Mir;
using Copeland.Script.Semantics;
using Copeland.Script.Semantics.Bound;
using Copeland.Script.Syntax;

namespace Copeland.Script.Compiler;

public static class CopelandCompiler
{
    public static CopelandCompilation Compile(string sourceText, CopelandCompilationOptions? options = null)
    {
        var effectiveOptions = options ?? new CopelandCompilationOptions();
        var diagnostics = new List<Diagnostic>();

        var syntaxTree = SyntaxTree.Parse(sourceText);
        diagnostics.AddRange(syntaxTree.Diagnostics);

        BoundCompilation? boundCompilation = null;
        MirCompilation? mirCompilation = null;
        CSharpCompilation? csharpCompilation = null;
        string? mirText = null;
        string? csharpText = null;

        if (effectiveOptions.TargetStage >= CopelandCompilationStage.Bound)
        {
            if (diagnostics.Count == 0)
            {
                boundCompilation = Binder.Bind(syntaxTree);
                diagnostics.AddRange(boundCompilation.Diagnostics);
            }
        }

        if (effectiveOptions.TargetStage >= CopelandCompilationStage.Mir)
        {
            if (diagnostics.Count == 0)
            {
                var mirProgram = LowerBoundProgram(boundCompilation!);
                mirCompilation = new MirCompilation(mirProgram, diagnostics.ToArray());
                mirText = MirTextWriter.Write(mirProgram);
            }
        }

        if (effectiveOptions.TargetStage >= CopelandCompilationStage.CSharp)
        {
            if (diagnostics.Count == 0 && mirCompilation?.Program is not null)
            {
                csharpCompilation = CSharpBackend.Emit(mirCompilation.Program);
                diagnostics.AddRange(csharpCompilation.Diagnostics);
                if (csharpCompilation.Diagnostics.Count == 0)
                {
                    csharpText = csharpCompilation.SourceText;
                }
            }
        }

        return new CopelandCompilation(
            effectiveOptions.TargetStage,
            diagnostics,
            syntaxTree,
            boundCompilation,
            mirCompilation,
            csharpCompilation,
            mirText,
            csharpText);
    }

    public static CopelandCompilation CompileToMir(string sourceText, CopelandCompilationOptions? options = null)
    {
        var effectiveOptions = options ?? new CopelandCompilationOptions();
        return Compile(sourceText, new CopelandCompilationOptions
        {
            TargetStage = CopelandCompilationStage.Mir,
            ModuleName = effectiveOptions.ModuleName,
        });
    }

    public static CopelandCompilation CompileToCSharp(string sourceText, CopelandCompilationOptions? options = null)
    {
        var effectiveOptions = options ?? new CopelandCompilationOptions();
        return Compile(sourceText, new CopelandCompilationOptions
        {
            TargetStage = CopelandCompilationStage.CSharp,
            ModuleName = effectiveOptions.ModuleName,
        });
    }

    private static MirProgram LowerBoundProgram(BoundCompilation bound)
    {
        var functions = bound.Program.Functions.Select(MirLowerFunction).ToArray();
        return new MirProgram(functions);
    }

    private static MirFunction MirLowerFunction(BoundFunctionDeclaration function)
    {
        var locals = new Dictionary<string, MirLocal>(StringComparer.Ordinal);
        var body = MirLowerStatements(function.Body.Statements, locals);
        return new MirFunction(
            function.Symbol.Name,
            function.Symbol.Parameters.Select(p => new MirParameter(p.Name, MirType.From(p.Type))).ToArray(),
            MirType.From(function.Symbol.ReturnType),
            function.Symbol.ErrorType is null ? null : MirType.From(function.Symbol.ErrorType),
            locals.Values.OrderBy(l => l.Name, StringComparer.Ordinal).ToArray(),
            body);
    }

    private static IReadOnlyList<MirStatement> MirLowerStatements(IReadOnlyList<BoundStatement> statements, Dictionary<string, MirLocal> locals)
        => statements.SelectMany(s => MirLowerStatement(s, locals)).ToArray();

    private static IReadOnlyList<MirStatement> MirLowerStatement(BoundStatement statement, Dictionary<string, MirLocal> locals)
    {
        return statement switch
        {
            BoundBlockStatement b => MirLowerStatements(b.Statements, locals),
            BoundVariableDeclaration v => [MirLowerVariable(v, locals)],
            BoundExpressionStatement e => [new MirExpressionStatement(MirLowerExpression(e.Expression))],
            BoundReturnStatement r => [new MirReturnStatement(r.Expression is null ? null : MirLowerExpression(r.Expression))],
            BoundIfStatement i => [new MirIfStatement(MirLowerExpression(i.Condition), MirLowerStatement(i.ThenStatement, locals), i.ElseStatement is null ? null : MirLowerStatement(i.ElseStatement, locals))],
            BoundWhileStatement w => [new MirWhileStatement(MirLowerExpression(w.Condition), MirLowerStatement(w.Body, locals))],
            BoundForStatement f => [new MirForStatement(f.Initializer is null ? null : MirLowerStatement(f.Initializer, locals).Single(), f.Condition is null ? null : MirLowerExpression(f.Condition), f.Increment is null ? null : MirLowerExpression(f.Increment), MirLowerStatement(f.Body, locals))],
            _ => []
        };
    }

    private static MirStatement MirLowerVariable(BoundVariableDeclaration v, Dictionary<string, MirLocal> locals)
    {
        var local = new MirLocal(v.Variable.Name, MirType.From(v.Variable.Type), v.Variable.IsReadOnly);
        locals.TryAdd(local.Name, local);
        return new MirVariableDeclarationStatement(local, MirLowerExpression(v.Initializer));
    }

    private static MirExpression MirLowerExpression(BoundExpression expression)
        => expression switch
        {
            BoundLiteralExpression l => new MirLiteralExpression(l.Value, MirType.From(l.Type)),
            BoundVariableExpression v => new MirVariableExpression(v.Variable.Name, MirType.From(v.Type)),
            BoundAssignmentExpression a => new MirAssignmentExpression(a.Variable.Name, MirLowerExpression(a.Expression), MirType.From(a.Type)),
            BoundUnaryExpression u => new MirUnaryExpression(OperatorName(u.OperatorKind), MirLowerExpression(u.Operand), MirType.From(u.Type)),
            BoundBinaryExpression b => new MirBinaryExpression(OperatorName(b.OperatorKind), MirLowerExpression(b.Left), MirLowerExpression(b.Right), MirType.From(b.Type)),
            BoundCallExpression c => new MirCallExpression(c.Function.Name, c.Arguments.Select(MirLowerExpression).ToArray(), MirType.From(c.Type), c.IsFallible, c.ErrorType is null ? null : MirType.From(c.ErrorType), false),
            BoundPropagateExpression p when p.Operand is BoundCallExpression c => new MirCallExpression(c.Function.Name, c.Arguments.Select(MirLowerExpression).ToArray(), MirType.From(c.Type), c.IsFallible, c.ErrorType is null ? null : MirType.From(c.ErrorType), true),
            BoundPropagateExpression p => MirLowerExpression(p.Operand),
            BoundArrayExpression a => new MirArrayExpression(a.Elements.Select(MirLowerExpression).ToArray(), MirType.From(a.Type)),
            _ => new MirLiteralExpression("<error>", new MirType("error"))
        };

    private static string OperatorName(SyntaxKind kind) => kind switch
    {
        SyntaxKind.PlusToken => "+",
        SyntaxKind.MinusToken => "-",
        SyntaxKind.StarToken => "*",
        SyntaxKind.SlashToken => "/",
        SyntaxKind.PercentToken => "%",
        SyntaxKind.BangToken => "!",
        SyntaxKind.LessToken => "<",
        SyntaxKind.LessOrEqualsToken => "<=",
        SyntaxKind.GreaterToken => ">",
        SyntaxKind.GreaterOrEqualsToken => ">=",
        SyntaxKind.EqualsEqualsToken => "==",
        SyntaxKind.BangEqualsToken => "!=",
        SyntaxKind.EqualsEqualsEqualsToken => "===",
        SyntaxKind.BangEqualsEqualsToken => "!==",
        SyntaxKind.AmpersandAmpersandToken => "&&",
        SyntaxKind.PipePipeToken => "||",
        _ => kind.ToString()
    };
}
