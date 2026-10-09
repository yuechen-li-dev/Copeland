using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Semantics;
using Copeland.TS.Syntax;

namespace Copeland.TS.Gpu;

internal sealed class GpuConstants
{
    private readonly GpuModuleGraph modules;
    private readonly Dictionary<string, (string Path, VariableDeclarationStatementSyntax Syntax)> sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VdMirExpression> values = new(StringComparer.Ordinal);
    private readonly HashSet<string> active = new(StringComparer.Ordinal);

    public GpuConstants(GpuModuleGraph modules)
    {
        this.modules = modules;
        foreach ((GpuSourceFile source, SyntaxTree tree) in modules.Sources)
        {
            foreach (var member in tree.Root.Members.OfType<GlobalStatementMemberSyntax>())
            {
                if (member.Statement is VariableDeclarationStatementSyntax declaration)
                {
                    sources.TryAdd(modules.Declare(source.Path, declaration.Identifier.Text), (source.Path, declaration));
                }
            }
        }
    }

    public VdMirExpression? Bind(string path, string name,
        Func<string, VariableDeclarationStatementSyntax, VdMirExpression> bind,
        Func<IEnumerable<VdMirFunction>> functions)
    {
        string identity = modules.Resolve(path, name);
        if (values.TryGetValue(identity, out VdMirExpression? value))
        {
            return value;
        }
        if (!sources.TryGetValue(identity, out var source))
        {
            return null;
        }
        if (source.Syntax.Keyword.Kind != SyntaxKind.ConstKeyword)
        {
            throw StaticEvaluationException.Ineligible($"Global shader value '{name}' must be const.");
        }
        if (!active.Add(identity))
        {
            throw StaticEvaluationException.Budget("Cyclic shader constant: " + identity);
        }
        try
        {
            value = GpuStaticEvaluation.Fold(bind(source.Path, source.Syntax), functions());
            values.Add(identity, value);
            return value;
        }
        finally
        {
            active.Remove(identity);
        }
    }
}

internal static class GpuReachability
{
    public static IReadOnlyList<VdMirFunction> Functions(IEnumerable<string> roots, IEnumerable<VdMirFunction> functions)
    {
        var byName = functions.ToDictionary(function => function.Name, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (string root in roots)
        {
            Visit(root);
        }
        return byName.Values.Where(function => visited.Contains(function.Name)).OrderBy(function => function.Name, StringComparer.Ordinal).ToArray();

        void Visit(string name)
        {
            if (!visited.Add(name) || !byName.TryGetValue(name, out VdMirFunction? function))
            {
                return;
            }
            Statements(function.Statements);
        }
        void Statements(IReadOnlyList<VdMirStatement> statements)
        {
            foreach (VdMirStatement statement in statements)
            {
                if (statement.Expression is not null)
                {
                    Expression(statement.Expression);
                }
                Statements(statement.Body ?? []);
                Statements(statement.ElseBody ?? []);
                if (statement.Initializer is not null)
                {
                    Statements([statement.Initializer]);
                }
                if (statement.Increment is not null)
                {
                    Statements([statement.Increment]);
                }
            }
        }
        void Expression(VdMirExpression expression)
        {
            if (expression.Kind == "call")
            {
                Visit(expression.Value!);
            }
            foreach (VdMirExpression operand in expression.Operands ?? [])
            {
                Expression(operand);
            }
        }
    }
}
