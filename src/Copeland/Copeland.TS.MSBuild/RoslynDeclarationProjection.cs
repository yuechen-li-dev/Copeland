using System.Reflection;
using Copeland.TS.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace Copeland.TS.MSBuild;

/// <summary>
/// Produces an in-memory metadata-only view of authored C# declarations. This
/// deliberately replaces executable bodies before emission: Copeland needs C#
/// symbols, never a temporary implementation assembly.
/// </summary>
internal static class RoslynDeclarationProjection
{
    public static bool TryCreate(
        IReadOnlyList<string> sourcePaths,
        IReadOnlyList<CopelandClrReference> references,
        string assemblyName,
        string generatedNamespace,
        string langVersion,
        string defineConstants,
        string nullable,
        out CopelandClrReference? declarationReference,
        out IReadOnlyList<RoslynDeclarationProjectionDiagnostic> diagnostics)
    {
        var reportedDiagnostics = new List<RoslynDeclarationProjectionDiagnostic>();
        CSharpParseOptions parseOptions = CreateParseOptions(langVersion, defineConstants);
        var declarationTrees = new List<SyntaxTree>();

        foreach (string sourcePath in sourcePaths)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(sourcePath), parseOptions, sourcePath);
            foreach (Diagnostic diagnostic in tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                reportedDiagnostics.Add(ToProjectionDiagnostic(diagnostic));
            }

            declarationTrees.Add(CSharpSyntaxTree.Create(
                (CSharpSyntaxNode)new DeclarationOnlyRewriter(generatedNamespace).Visit(tree.GetRoot())!,
                parseOptions,
                sourcePath));
        }

        if (reportedDiagnostics.Count > 0)
        {
            declarationReference = null;
            diagnostics = reportedDiagnostics;
            return false;
        }

        string[] metadataPaths = references
            .Where(reference => reference.AssemblyPath is not null && File.Exists(reference.AssemblyPath))
            .Select(reference => reference.AssemblyPath!)
            .Append(typeof(object).Assembly.Location)
            .Concat(GetTrustedPlatformAssemblyPaths())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        MetadataReference[] metadataReferences = metadataPaths
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName,
            declarationTrees,
            metadataReferences,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: ParseNullable(nullable),
                deterministic: true));

        using var stream = new MemoryStream();
        EmitResult result = compilation.Emit(stream, options: new EmitOptions(metadataOnly: true));
        if (!result.Success)
        {
            foreach (Diagnostic diagnostic in result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                reportedDiagnostics.Add(ToProjectionDiagnostic(diagnostic));
            }

            declarationReference = null;
            diagnostics = reportedDiagnostics;
            return false;
        }

        Assembly declarations = Assembly.Load(stream.ToArray());
        declarationReference = new CopelandClrReference(null, declarations, IncludeInternalSymbols: true);
        diagnostics = [];
        return true;
    }

    private static CSharpParseOptions CreateParseOptions(string langVersion, string defineConstants)
    {
        LanguageVersion version = LanguageVersion.Preview;
        if (!string.IsNullOrWhiteSpace(langVersion)
            && LanguageVersionFacts.TryParse(langVersion, out LanguageVersion parsedVersion))
        {
            version = parsedVersion;
        }

        string[] symbols = defineConstants
            .Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new CSharpParseOptions(version, DocumentationMode.None, SourceCodeKind.Regular, symbols);
    }

    private static NullableContextOptions ParseNullable(string nullable)
        => nullable.Trim().ToLowerInvariant() switch
        {
            "enable" or "annotations" or "warnings" => NullableContextOptions.Enable,
            "disable" => NullableContextOptions.Disable,
            _ => NullableContextOptions.Disable,
        };

    private static IEnumerable<string> GetTrustedPlatformAssemblyPaths()
    {
        string? paths = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        return paths?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [];
    }

    private static RoslynDeclarationProjectionDiagnostic ToProjectionDiagnostic(Diagnostic diagnostic)
    {
        FileLinePositionSpan location = diagnostic.Location.GetLineSpan();
        return new RoslynDeclarationProjectionDiagnostic(
            diagnostic.Id,
            diagnostic.GetMessage(),
            location.Path,
            location.StartLinePosition.Line + 1,
            location.StartLinePosition.Character + 1);
    }

    private sealed class DeclarationOnlyRewriter(string generatedNamespace) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitGlobalStatement(GlobalStatementSyntax node) => null;

        public override SyntaxNode? VisitAttributeList(AttributeListSyntax node) => null;

        public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
            => string.Equals(node.Name?.ToString(), generatedNamespace, StringComparison.Ordinal)
                ? null
                : base.VisitUsingDirective(node);

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
            => node.Body is null && node.ExpressionBody is null
                ? base.VisitMethodDeclaration(node)
                : node.WithBody(ThrowBody()).WithExpressionBody(null).WithSemicolonToken(default);

        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
            => node.WithBody(ThrowBody()).WithExpressionBody(null).WithSemicolonToken(default);

        public override SyntaxNode? VisitDestructorDeclaration(DestructorDeclarationSyntax node)
            => node.WithBody(ThrowBody()).WithExpressionBody(null).WithSemicolonToken(default);

        public override SyntaxNode? VisitOperatorDeclaration(OperatorDeclarationSyntax node)
            => node.WithBody(ThrowBody()).WithExpressionBody(null).WithSemicolonToken(default);

        public override SyntaxNode? VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
            => node.WithBody(ThrowBody()).WithExpressionBody(null).WithSemicolonToken(default);

        public override SyntaxNode? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
            => node.Body is null && node.ExpressionBody is null
                ? base.VisitAccessorDeclaration(node)
                : node.WithBody(ThrowBody()).WithExpressionBody(null).WithSemicolonToken(default);

        public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
            => node.WithDeclaration(RemoveInitializers(node.Declaration));

        public override SyntaxNode? VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
            => node.WithDeclaration(RemoveInitializers(node.Declaration));

        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
            => node.WithInitializer(null);

        public override SyntaxNode? VisitIndexerDeclaration(IndexerDeclarationSyntax node)
            => node.WithExpressionBody(null);

        private static VariableDeclarationSyntax RemoveInitializers(VariableDeclarationSyntax declaration)
            => declaration.WithVariables(SyntaxFactory.SeparatedList(
                declaration.Variables.Select(variable => variable.WithInitializer(null))));

        private static BlockSyntax ThrowBody()
            => SyntaxFactory.Block(SyntaxFactory.ThrowStatement(
                SyntaxFactory.PostfixUnaryExpression(
                    SyntaxKind.SuppressNullableWarningExpression,
                    SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))));
    }
}

internal sealed record RoslynDeclarationProjectionDiagnostic(
    string Id,
    string Message,
    string FilePath,
    int Line,
    int Column);
